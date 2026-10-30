namespace QuizPollApi.Features.Quizzes;

public static class GetQuizResults
{
    public sealed record Query(int QuizId, string? AccessCode, string? SessionToken) : IQuery<QuizResultsDto>;

    public sealed class Handler(IQuizAccess access, IAnalyticsService analytics) : IQueryHandler<Query, QuizResultsDto>
    {
        public async Task<QuizResultsDto> Handle(Query query, CancellationToken ct)
        {
            var quiz = await access.GetViewableAsync(query.QuizId, query.AccessCode, ct);
            await access.EnsureCanViewResultsAsync(quiz, query.SessionToken, ct);

            var results = await analytics.GetResultsAsync(quiz.Id, ct);
            var reveal = access.IsCreator(quiz) || quiz.IsClosed(DateTime.UtcNow);
            return results.WithCorrectness(quiz, reveal);
        }
    }
}
