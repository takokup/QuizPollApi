namespace QuizPollApi.Contracts;
public sealed record OptionInput(string Text, bool IsCorrect = false);

public sealed record QuestionInput(
    string Text,
    QuestionType Type,
    bool IsRequired = true,
    int? TimeLimitSeconds = null,
    string? ImageUrl = null,
    int? MaxRating = null,
    List<OptionInput>? Options = null);

public sealed record AnswerDto(int QuestionId, int[]? SelectedOptionIds = null, string? TextAnswer = null, int? RatingValue = null);
public sealed record OptionDto(int Id, string Text, int OrderIndex, bool? IsCorrect);

public sealed record QuestionDto(
    int Id, string Text, QuestionType Type, int OrderIndex, bool IsRequired,
    int? TimeLimitSeconds, string? ImageUrl, int? MaxRating, List<OptionDto> Options);

public sealed record QuizSummaryDto(
    int Id, string Title, string? Description, QuizType Type, QuizVisibility Visibility, QuizStatus Status,
    DateTime? StartsAt, DateTime? EndsAt, DateTime CreatedAt, int ResponseCount, int CreatorId);

public sealed record QuizDetailDto(
    int Id, int CreatorId, string Title, string? Description, QuizType Type, QuizVisibility Visibility,
    string? AccessCode, bool IsAnonymousAllowed, QuizStatus Status, DateTime? StartsAt, DateTime? EndsAt,
    ResultVisibility ResultVisibility, List<QuestionDto> Questions);

public sealed record QuizCreatedDto(int Id, string? AccessCode);

public sealed record PagedResult<T>(List<T> Items, int Page, int PageSize, int TotalCount);

public sealed record StartResponseResult(int ResponseId, string? SessionToken, DateTime StartedAt);

public sealed record ResponseSubmittedDto(int ResponseId, int? Score, int? MaxScore);

public sealed record ResponseDto(
    int Id, int QuizId, int? UserId, DateTime StartedAt, DateTime? CompletedAt, int? Score, List<AnswerDto> Answers);

public sealed record OptionResultDto(int OptionId, string Text, int Count, double Percentage, bool? IsCorrect);

public sealed record RatingBucketDto(int Value, int Count);

public sealed record QuestionStatsDto(
    int QuestionId, string Text, QuestionType Type, int AnswerCount, List<OptionResultDto> Options,
    double? AverageRating, double? MedianRating, double? StdDevRating,
    List<RatingBucketDto> RatingDistribution, int TextAnswerCount);

public sealed record QuizResultsDto(int QuizId, int TotalResponses, DateTime GeneratedAt, List<QuestionStatsDto> Questions);

public sealed record TimeBucketDto(DateTime Bucket, int Count);

public sealed record TrendDto(
    string Granularity, int CurrentBucketCount, int PreviousBucketCount, double? ChangePercent, double MovingAverage3);

public sealed record DemographicBucketDto(string Label, int Count, double Percentage);

public sealed record DemographicBreakdownDto(string Dimension, List<DemographicBucketDto> Buckets);
public sealed record TimeHeatmapDto(int[][] Counts, int MaxCount);

public sealed record HeatCellDto(string Label, int Count, double Percentage);

public sealed record AnswerHeatmapRowDto(int QuestionId, string Question, List<HeatCellDto> Cells);

public sealed record QuizAnalyticsDto(
    int QuizId, int TotalResponses, double AverageCompletionSeconds, double CompletionRate, DateTime LastCalculatedAt,
    List<TimeBucketDto> Timeline, TrendDto Trend, List<DemographicBreakdownDto> Demographics,
    TimeHeatmapDto TimeHeatmap, List<AnswerHeatmapRowDto> AnswerHeatmap);

public sealed record ExportFile(byte[] Content, string ContentType, string FileName, bool Truncated);
public sealed record AuthResultDto(string Token, DateTime ExpiresAt, int UserId, string DisplayName);
public sealed record ResponseUpdate(int QuizId, int TotalResponses, int ParticipantCount, QuizResultsDto? LatestStats);
public sealed record QuestionChangedNotification(
    int QuizId, int QuestionId, int QuestionIndex, DateTime StartedAt, DateTime? EndsAt);
public sealed record QuizClosedNotification(int QuizId, DateTime ClosedAt, int TotalResponses, QuizResultsDto? FinalResults);
