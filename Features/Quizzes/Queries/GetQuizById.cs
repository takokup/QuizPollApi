namespace QuizPollApi.Features.Quizzes;

public static class GetQuizById
{
    public sealed record Query(int QuizId, string? AccessCode) : IQuery<QuizDetailDto>;

    public sealed class Handler(IQuizAccess access) : IQueryHandler<Query, QuizDetailDto>
    {
        public async Task<QuizDetailDto> Handle(Query query, CancellationToken ct)
        {
            var quiz = await access.GetViewableAsync(query.QuizId, query.AccessCode, ct);
            return quiz.ToDetail(access.IsCreator(quiz), DateTime.UtcNow);
        }
    }
}
