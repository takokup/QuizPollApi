namespace QuizPollApi.Infrastructure.Services;

public interface IAnalyticsService
{
    Task<QuizResultsDto> GetResultsAsync(int quizId, CancellationToken ct);

    Task RecalculateAsync(int quizId, CancellationToken ct);

    Task<QuizAnalyticsDto> GetAnalyticsAsync(int quizId, string granularity, DateTime? from, DateTime? to, CancellationToken ct);
}

public sealed class AnalyticsService(
    ReadDbContext readDb,
    AppDbContext db,
    IQuizCatalog catalog,
    ICacheService cache) : IAnalyticsService
{

    public async Task<QuizResultsDto> GetResultsAsync(int quizId, CancellationToken ct) =>
        (await cache.GetOrCreateAsync(
            CacheKeys.QuizResults(quizId),
            () => ComputeResultsAsync(quizId, ct),
            TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(1),
            ct))!;

    private async Task<QuizResultsDto?> ComputeResultsAsync(int quizId, CancellationToken ct)
    {
        var quiz = await catalog.GetAsync(quizId, ct) ?? throw new NotFoundException("Quiz not found.");

        var total = (await Sql.QueryAsync(readDb,
            "SELECT COUNT(*)::int FROM responses WHERE quiz_id = @quiz AND completed_at IS NOT NULL",
            r => r.GetInt32(0), ct, ("quiz", quizId))).First();

        var optionCounts = (await Sql.QueryAsync(readDb, """
                SELECT ra.question_id, o AS option_id, COUNT(*)::int
                FROM response_answers ra
                JOIN responses r ON r.id = ra.response_id
                CROSS JOIN LATERAL unnest(ra.selected_option_ids) AS o
                WHERE r.quiz_id = @quiz AND r.completed_at IS NOT NULL
                GROUP BY ra.question_id, o
                """,
                r => (QuestionId: r.GetInt32(0), OptionId: r.GetInt32(1), Count: r.GetInt32(2)), ct, ("quiz", quizId)))
            .ToDictionary(x => (x.QuestionId, x.OptionId), x => x.Count);

        var ratingCounts = (await Sql.QueryAsync(readDb, """
                SELECT ra.question_id, ra.rating_value, COUNT(*)::int
                FROM response_answers ra
                JOIN responses r ON r.id = ra.response_id
                WHERE r.quiz_id = @quiz AND r.completed_at IS NOT NULL AND ra.rating_value IS NOT NULL
                GROUP BY ra.question_id, ra.rating_value
                """,
                r => (QuestionId: r.GetInt32(0), Value: r.GetInt32(1), Count: r.GetInt32(2)), ct, ("quiz", quizId)))
            .ToLookup(x => x.QuestionId);

        var answered = (await Sql.QueryAsync(readDb, """
                SELECT ra.question_id, COUNT(*)::int
                FROM response_answers ra
                JOIN responses r ON r.id = ra.response_id
                WHERE r.quiz_id = @quiz AND r.completed_at IS NOT NULL
                GROUP BY ra.question_id
                """,
                r => (QuestionId: r.GetInt32(0), Count: r.GetInt32(1)), ct, ("quiz", quizId)))
            .ToDictionary(x => x.QuestionId, x => x.Count);

        var questions = new List<QuestionStatsDto>();
        foreach (var q in quiz.Questions)
        {
            var answerCount = answered.GetValueOrDefault(q.Id);

            var options = q.Options.Select(o =>
            {
                var count = optionCounts.GetValueOrDefault((q.Id, o.Id));
                return new OptionResultDto(o.Id, o.Text, count, Percent(count, answerCount), null);
            }).ToList();

            var distribution = new List<RatingBucketDto>();
            double? mean = null, median = null, stdDev = null;
            if (q.Type == QuestionType.Rating)
            {
                var counts = ratingCounts[q.Id].ToDictionary(x => x.Value, x => x.Count);
                for (var v = 1; v <= q.MaxRating; v++)
                    distribution.Add(new RatingBucketDto(v, counts.GetValueOrDefault(v)));

                var n = distribution.Sum(d => d.Count);
                if (n > 0)
                {
                    mean = distribution.Sum(d => (double)d.Value * d.Count) / n;
                    stdDev = Math.Sqrt(distribution.Sum(d => d.Count * Math.Pow(d.Value - mean.Value, 2)) / n);
                    median = Median(distribution, n);
                    mean = Math.Round(mean.Value, 2);
                    stdDev = Math.Round(stdDev.Value, 2);
                }
            }

            questions.Add(new QuestionStatsDto(
                q.Id, q.Text, q.Type, answerCount, options, mean, median, stdDev, distribution,
                q.Type == QuestionType.Text ? answerCount : 0));
        }

        return new QuizResultsDto(quizId, total, DateTime.UtcNow, questions);
    }

    private static double Percent(int count, int total) => total == 0 ? 0 : Math.Round(count * 100.0 / total, 1);

    private static double Median(List<RatingBucketDto> distribution, int n)
    {
        double ValueAt(int index) // 0-based position in the sorted sample
        {
            var seen = 0;
            foreach (var d in distribution)
            {
                seen += d.Count;
                if (index < seen) return d.Value;
            }
            return distribution[^1].Value;
        }

        return n % 2 == 1 ? ValueAt(n / 2) : (ValueAt(n / 2 - 1) + ValueAt(n / 2)) / 2.0;
    }


    public async Task RecalculateAsync(int quizId, CancellationToken ct)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO quiz_analytics (quiz_id, total_responses, average_completion_time, completion_rate, last_calculated_at)
            SELECT {quizId},
                   (COUNT(*) FILTER (WHERE completed_at IS NOT NULL))::int,
                   COALESCE(AVG(EXTRACT(EPOCH FROM (completed_at - started_at))) FILTER (WHERE completed_at IS NOT NULL), 0)::float8,
                   (CASE WHEN COUNT(*) = 0 THEN 0 ELSE (COUNT(*) FILTER (WHERE completed_at IS NOT NULL))::float8 / COUNT(*) END)::float8,
                   now()
            FROM responses
            WHERE quiz_id = {quizId}
            ON CONFLICT (quiz_id) DO UPDATE SET
                total_responses = EXCLUDED.total_responses,
                average_completion_time = EXCLUDED.average_completion_time,
                completion_rate = EXCLUDED.completion_rate,
                last_calculated_at = EXCLUDED.last_calculated_at
            """, ct);
    }


    public async Task<QuizAnalyticsDto> GetAnalyticsAsync(
        int quizId, string granularity, DateTime? from, DateTime? to, CancellationToken ct)
    {
        granularity = granularity.ToLowerInvariant();
        Guard.That(granularity is "hour" or "day", "granularity must be 'hour' or 'day'.");

        var end = (to ?? DateTime.UtcNow).ToUtc();
        var start = (from ?? (granularity == "hour" ? end.AddHours(-48) : end.AddDays(-30))).ToUtc();
        Guard.That(start < end, "'from' must be earlier than 'to'.");
        var buckets = (end - start).TotalHours / (granularity == "hour" ? 1 : 24);
        Guard.That(buckets <= 1000, "The requested range is too large for this granularity (max 1000 buckets).");

        var keyFrom = start.Ticks / TimeSpan.TicksPerMinute;
        var keyTo = end.Ticks / TimeSpan.TicksPerMinute;

        return (await cache.GetOrCreateAsync(
            CacheKeys.QuizAnalytics(quizId, granularity, keyFrom, keyTo),
            () => BuildAnalyticsAsync(quizId, granularity, start, end, ct),
            TimeSpan.FromMinutes(1),
            ct: ct))!;
    }

    private async Task<QuizAnalyticsDto?> BuildAnalyticsAsync(int quizId, string granularity, DateTime start, DateTime end, CancellationToken ct)
    {
        _ = await catalog.GetAsync(quizId, ct) ?? throw new NotFoundException("Quiz not found.");

        var summary = await db.QuizAnalytics.AsNoTracking().FirstOrDefaultAsync(a => a.QuizId == quizId, ct);
        if (summary is null)
        {
            await RecalculateAsync(quizId, ct);
            summary = await db.QuizAnalytics.AsNoTracking().FirstAsync(a => a.QuizId == quizId, ct);
        }

        var results = await GetResultsAsync(quizId, ct);
        var timeline = await BuildTimelineAsync(quizId, granularity, start, end, ct);

        return new QuizAnalyticsDto(
            quizId, summary.TotalResponses, Math.Round(summary.AverageCompletionTime, 1),
            Math.Round(summary.CompletionRate, 3), summary.LastCalculatedAt,
            timeline,
            BuildTrend(granularity, timeline),
            await BuildDemographicsAsync(quizId, ct),
            await BuildTimeHeatmapAsync(quizId, ct),
            BuildAnswerHeatmap(results));
    }

    private async Task<List<TimeBucketDto>> BuildTimelineAsync(int quizId, string granularity, DateTime start, DateTime end, CancellationToken ct)
    {
        var sql = $"""
            SELECT date_trunc('{granularity}', completed_at AT TIME ZONE 'UTC') AS bucket, COUNT(*)::int
            FROM responses
            WHERE quiz_id = @quiz AND completed_at IS NOT NULL AND completed_at >= @from AND completed_at < @to
            GROUP BY 1
            """;

        var rows = (await Sql.QueryAsync(readDb, sql,
                r => (Bucket: DateTime.SpecifyKind(r.GetDateTime(0), DateTimeKind.Utc), Count: r.GetInt32(1)), ct,
                ("quiz", quizId), ("from", start), ("to", end)))
            .ToDictionary(x => x.Bucket.Ticks, x => x.Count);

        var step = granularity == "hour" ? TimeSpan.FromHours(1) : TimeSpan.FromDays(1);
        var cursor = granularity == "hour"
            ? new DateTime(start.Year, start.Month, start.Day, start.Hour, 0, 0, DateTimeKind.Utc)
            : new DateTime(start.Year, start.Month, start.Day, 0, 0, 0, DateTimeKind.Utc);

        var timeline = new List<TimeBucketDto>();
        for (; cursor < end; cursor += step)
            timeline.Add(new TimeBucketDto(cursor, rows.GetValueOrDefault(cursor.Ticks)));
        return timeline;
    }

    private static TrendDto BuildTrend(string granularity, List<TimeBucketDto> timeline)
    {
        var complete = timeline.Count > 2 ? timeline[..^1] : timeline;
        var current = complete.Count > 0 ? complete[^1].Count : 0;
        var previous = complete.Count > 1 ? complete[^2].Count : 0;
        double? change = previous > 0 ? Math.Round((current - previous) * 100.0 / previous, 1) : null;
        var movingAverage = complete.Count == 0 ? 0 : Math.Round(complete.TakeLast(3).Average(b => b.Count), 2);
        return new TrendDto(granularity, current, previous, change, movingAverage);
    }

    private async Task<List<DemographicBreakdownDto>> BuildDemographicsAsync(int quizId, CancellationToken ct)
    {
        var list = new List<DemographicBreakdownDto>
        {
            await Breakdown("respondent_type", """
                SELECT CASE WHEN r.user_id IS NULL THEN 'anonymous' ELSE 'authenticated' END, COUNT(*)::int
                FROM responses r
                WHERE r.quiz_id = @quiz AND r.completed_at IS NOT NULL
                GROUP BY 1
                """, quizId, ct)
        };

        foreach (var (dimension, column) in new[] { ("country", "country"), ("age_group", "age_group"), ("gender", "gender") })
        {
            list.Add(await Breakdown(dimension, $"""
                SELECT COALESCE(NULLIF(u.{column}, ''), 'unknown'), COUNT(*)::int
                FROM responses r
                JOIN users u ON u.id = r.user_id
                WHERE r.quiz_id = @quiz AND r.completed_at IS NOT NULL
                GROUP BY 1
                """, quizId, ct));
        }

        return list;
    }

    private async Task<DemographicBreakdownDto> Breakdown(string dimension, string sql, int quizId, CancellationToken ct)
    {
        var rows = await Sql.QueryAsync(readDb, sql, r => (Label: r.GetString(0), Count: r.GetInt32(1)), ct, ("quiz", quizId));
        var total = rows.Sum(x => x.Count);
        return new DemographicBreakdownDto(
            dimension,
            rows.OrderByDescending(x => x.Count).Select(x => new DemographicBucketDto(x.Label, x.Count, Percent(x.Count, total))).ToList());
    }

    private async Task<TimeHeatmapDto> BuildTimeHeatmapAsync(int quizId, CancellationToken ct)
    {
        var rows = await Sql.QueryAsync(readDb, """
                SELECT EXTRACT(DOW FROM completed_at AT TIME ZONE 'UTC')::int,
                       EXTRACT(HOUR FROM completed_at AT TIME ZONE 'UTC')::int,
                       COUNT(*)::int
                FROM responses
                WHERE quiz_id = @quiz AND completed_at IS NOT NULL
                GROUP BY 1, 2
                """,
            r => (Day: r.GetInt32(0), Hour: r.GetInt32(1), Count: r.GetInt32(2)), ct, ("quiz", quizId));

        var counts = Enumerable.Range(0, 7).Select(_ => new int[24]).ToArray();
        foreach (var row in rows) counts[row.Day][row.Hour] = row.Count;
        return new TimeHeatmapDto(counts, rows.Count == 0 ? 0 : rows.Max(x => x.Count));
    }

    private static List<AnswerHeatmapRowDto> BuildAnswerHeatmap(QuizResultsDto results) =>
        results.Questions
            .Where(q => q.Type != QuestionType.Text)
            .Select(q => new AnswerHeatmapRowDto(
                q.QuestionId, q.Text,
                q.Type == QuestionType.Rating
                    ? q.RatingDistribution.Select(d => new HeatCellDto(d.Value.ToString(), d.Count, Percent(d.Count, q.AnswerCount))).ToList()
                    : q.Options.Select(o => new HeatCellDto(o.Text, o.Count, o.Percentage)).ToList()))
            .ToList();
}
