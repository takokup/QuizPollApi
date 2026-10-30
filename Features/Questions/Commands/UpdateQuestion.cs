namespace QuizPollApi.Features.Questions;

public static class UpdateQuestion
{
    public sealed record Command(int QuizId, int QuestionId, QuestionInput Question) : ICommand<Unit>;

    public sealed class Handler(AppDbContext db, ICurrentUser user, IEventPublisher events) : ICommandHandler<Command, Unit>
    {
        public async Task<Unit> Handle(Command cmd, CancellationToken ct)
        {
            var userId = user.RequireUserId();
            var quiz = await db.Quizzes.FirstOrDefaultAsync(q => q.Id == cmd.QuizId, ct)
                       ?? throw new NotFoundException("Quiz not found.");

            if (quiz.CreatorId != userId) throw new ForbiddenException("Only the creator can edit this quiz.");
            if (quiz.IsActive || quiz.ClosedAt is not null)
                throw new ConflictException("Questions can only be changed while the quiz is a draft.");

            var question = await db.Questions.Include(q => q.Options)
                               .FirstOrDefaultAsync(q => q.Id == cmd.QuestionId && q.QuizId == quiz.Id, ct)
                           ?? throw new NotFoundException("Question not found.");

            QuestionRules.Validate(cmd.Question, quiz.Type);

            db.AnswerOptions.RemoveRange(question.Options);
            QuestionRules.Apply(question, cmd.Question, question.OrderIndex);

            await db.SaveChangesAsync(ct);
            await events.PublishAsync(new QuizUpdated(quiz.Id), ct);
            return Unit.Value;
        }
    }
}

public static class DeleteQuestion
{
    public sealed record Command(int QuizId, int QuestionId) : ICommand<Unit>;

    public sealed class Handler(AppDbContext db, ICurrentUser user, IEventPublisher events) : ICommandHandler<Command, Unit>
    {
        public async Task<Unit> Handle(Command cmd, CancellationToken ct)
        {
            var userId = user.RequireUserId();
            var quiz = await db.Quizzes.FirstOrDefaultAsync(q => q.Id == cmd.QuizId, ct)
                       ?? throw new NotFoundException("Quiz not found.");

            if (quiz.CreatorId != userId) throw new ForbiddenException("Only the creator can edit this quiz.");
            if (quiz.IsActive || quiz.ClosedAt is not null)
                throw new ConflictException("Questions can only be changed while the quiz is a draft.");

            var question = await db.Questions.FirstOrDefaultAsync(q => q.Id == cmd.QuestionId && q.QuizId == quiz.Id, ct)
                           ?? throw new NotFoundException("Question not found.");

            db.Questions.Remove(question);
            await db.SaveChangesAsync(ct);
            await events.PublishAsync(new QuizUpdated(quiz.Id), ct);
            return Unit.Value;
        }
    }
}
