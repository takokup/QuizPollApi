namespace QuizPollApi.Features.Analytics;

public static class GetQuizAnalytics
{
    public sealed record Query(int QuizId, string Granularity = "hour", DateTime? From = null, DateTime? To = null)
        : IQuery<QuizAnalyticsDto>;

    public sealed class Handler(IQuizCatalog catalog, IAnalyticsService analytics, ICurrentUser user)
        : IQueryHandler<Query, QuizAnalyticsDto>
    {
        public async Task<QuizAnalyticsDto> Handle(Query query, CancellationToken ct)
        {
            var userId = user.RequireUserId();
            var quiz = await catalog.GetAsync(query.QuizId, ct) ?? throw new NotFoundException("Quiz not found.");
            if (quiz.CreatorId != userId) throw new ForbiddenException("Only the creator can view analytics.");

            return await analytics.GetAnalyticsAsync(quiz.Id, query.Granularity, query.From, query.To, ct);
        }
    }
}
