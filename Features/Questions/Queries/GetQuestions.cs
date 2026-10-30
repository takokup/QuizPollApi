namespace QuizPollApi.Features.Questions;

public static class GetQuestions
{
    public sealed record Query(int QuizId, string? AccessCode) : IQuery<List<QuestionDto>>;

    public sealed class Handler(IQuizAccess access) : IQueryHandler<Query, List<QuestionDto>>
    {
        public async Task<List<QuestionDto>> Handle(Query query, CancellationToken ct)
        {
            var quiz = await access.GetViewableAsync(query.QuizId, query.AccessCode, ct);
            var reveal = access.IsCreator(quiz) || quiz.IsClosed(DateTime.UtcNow);
            return quiz.Questions.OrderBy(q => q.OrderIndex).Select(q => q.ToDto(reveal)).ToList();
        }
    }
}
