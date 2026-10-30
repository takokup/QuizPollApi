namespace QuizPollApi.Features.Analytics;
public static class GetQuestionStats
{
    public sealed record Query(int QuizId, int? QuestionId = null) : IQuery<List<QuestionStatsDto>>;

    public sealed class Handler(IQuizCatalog catalog, IAnalyticsService analytics, ICurrentUser user)
        : IQueryHandler<Query, List<QuestionStatsDto>>
    {
        public async Task<List<QuestionStatsDto>> Handle(Query query, CancellationToken ct)
        {
            var userId = user.RequireUserId();
            var quiz = await catalog.GetAsync(query.QuizId, ct) ?? throw new NotFoundException("Quiz not found.");
            if (quiz.CreatorId != userId) throw new ForbiddenException("Only the creator can view question statistics.");

            var results = (await analytics.GetResultsAsync(quiz.Id, ct)).WithCorrectness(quiz, reveal: true);

            if (query.QuestionId is null) return results.Questions;

            var one = results.Questions.Where(q => q.QuestionId == query.QuestionId).ToList();
            if (one.Count == 0) throw new NotFoundException("Question not found.");
            return one;
        }
    }
}
