namespace QuizPollApi.Features.Questions;

public static class AddQuestion
{
    public sealed record Command(int QuizId, QuestionInput Question) : ICommand<int>;

    public sealed class Handler(AppDbContext db, ICurrentUser user, IEventPublisher events) : ICommandHandler<Command, int>
    {
        public async Task<int> Handle(Command cmd, CancellationToken ct)
        {
            var userId = user.RequireUserId();
            var quiz = await db.Quizzes.FirstOrDefaultAsync(q => q.Id == cmd.QuizId, ct)
                       ?? throw new NotFoundException("Quiz not found.");

            if (quiz.CreatorId != userId) throw new ForbiddenException("Only the creator can edit this quiz.");
            if (quiz.IsActive || quiz.ClosedAt is not null)
                throw new ConflictException("Questions can only be changed while the quiz is a draft.");

            QuestionRules.Validate(cmd.Question, quiz.Type);

            var count = await db.Questions.CountAsync(q => q.QuizId == quiz.Id, ct);
            Guard.That(count < QuestionRules.MaxQuestionsPerQuiz, $"At most {QuestionRules.MaxQuestionsPerQuiz} questions per quiz.");

            var next = count == 0 ? 0 : await db.Questions.Where(q => q.QuizId == quiz.Id).MaxAsync(q => q.OrderIndex, ct) + 1;
            var question = QuestionRules.Build(cmd.Question, next);
            question.QuizId = quiz.Id;

            db.Questions.Add(question);
            await db.SaveChangesAsync(ct);

            await events.PublishAsync(new QuizUpdated(quiz.Id), ct);
            return question.Id;
        }
    }
}
