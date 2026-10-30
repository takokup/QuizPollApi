using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace QuizPollApi.Features.Analytics;

public static class ExportResults
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public sealed record Query(int QuizId, string Format) : IQuery<ExportFile>;

    private sealed record Row(
        int ResponseId, int? UserId, DateTime CompletedAt, int? Score, int QuestionId,
        int[] SelectedOptionIds, string? TextAnswer, int? RatingValue);

    public sealed class Handler(ReadDbContext readDb, IQuizCatalog catalog, ICurrentUser user, IOptions<ExportOptions> options)
        : IQueryHandler<Query, ExportFile>
    {
        public async Task<ExportFile> Handle(Query query, CancellationToken ct)
        {
            var userId = user.RequireUserId();
            var format = (query.Format ?? "").ToLowerInvariant();
            Guard.That(format is "csv" or "json", "format must be 'csv' or 'json'.");

            var quiz = await catalog.GetAsync(query.QuizId, ct) ?? throw new NotFoundException("Quiz not found.");
            if (quiz.CreatorId != userId) throw new ForbiddenException("Only the creator can export results.");

            var max = options.Value.MaxRows;
            var rows = await Sql.QueryAsync(readDb, """
                    SELECT r.id, r.user_id, r.completed_at, r.score, ra.question_id,
                           ra.selected_option_ids, ra.text_answer, ra.rating_value
                    FROM responses r
                    JOIN response_answers ra ON ra.response_id = r.id
                    WHERE r.quiz_id = @quiz AND r.completed_at IS NOT NULL
                    ORDER BY r.id, ra.question_id
                    LIMIT @limit
                    """,
                r => new Row(
                    r.GetInt32(0),
                    r.IsDBNull(1) ? null : r.GetInt32(1),
                    DateTime.SpecifyKind(r.GetDateTime(2), DateTimeKind.Utc),
                    r.IsDBNull(3) ? null : r.GetInt32(3),
                    r.GetInt32(4),
                    r.IsDBNull(5) ? [] : r.GetFieldValue<int[]>(5),
                    r.IsDBNull(6) ? null : r.GetString(6),
                    r.IsDBNull(7) ? null : r.GetInt32(7)),
                ct, ("quiz", quiz.Id), ("limit", max + 1));

            var truncated = rows.Count > max;
            if (truncated) rows.RemoveAt(rows.Count - 1);

            var questions = quiz.Questions.ToDictionary(q => q.Id);

            string AnswerText(Row row)
            {
                if (!questions.TryGetValue(row.QuestionId, out var q)) return "";
                if (row.RatingValue is { } rating) return rating.ToString();
                if (row.TextAnswer is not null) return row.TextAnswer;
                var names = row.SelectedOptionIds.Select(id => q.Options.FirstOrDefault(o => o.Id == id)?.Text ?? $"#{id}");
                return string.Join("; ", names);
            }

            var baseName = $"quiz-{quiz.Id}-results";

            if (format == "json")
            {
                var data = rows.Select(r => new
                {
                    r.ResponseId, r.UserId, r.CompletedAt, r.Score, r.QuestionId,
                    Question = questions.GetValueOrDefault(r.QuestionId)?.Text,
                    Answer = AnswerText(r)
                });
                var bytes = JsonSerializer.SerializeToUtf8Bytes(
                    new { quizId = quiz.Id, title = quiz.Title, truncated, rows = data }, Json);
                return new ExportFile(bytes, "application/json", baseName + ".json", truncated);
            }

            var sb = new StringBuilder("response_id,user_id,completed_at,score,question_id,question,answer\r\n");
            foreach (var r in rows)
            {
                sb.Append(r.ResponseId).Append(',')
                  .Append(r.UserId).Append(',')
                  .Append(r.CompletedAt.ToString("O")).Append(',')
                  .Append(r.Score).Append(',')
                  .Append(r.QuestionId).Append(',')
                  .Append(Csv(questions.GetValueOrDefault(r.QuestionId)?.Text)).Append(',')
                  .Append(Csv(AnswerText(r))).Append("\r\n");
            }

            // UTF-8 BOM so Excel opens non-Latin (e.g. Georgian) text correctly
            var csv = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
            return new ExportFile(csv, "text/csv; charset=utf-8", baseName + ".csv", truncated);
        }

        private static string Csv(string? value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            if (value[0] is '=' or '+' or '-' or '@' or '\t' or '\r') value = "'" + value;
            return value.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
        }
    }
}
