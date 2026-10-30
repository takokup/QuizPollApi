namespace QuizPollApi.Features.Quizzes;

public static class CloseQuiz
{
    public sealed record Command(int QuizId) : ICommand<Unit>;

    public sealed class Handler(AppDbContext db, ICurrentUser user, IEventPublisher events)
        : ICommandHandler<Command, Unit>
    {
        public async Task<Unit> Handle(Command cmd, CancellationToken ct)
        {
            var userId = user.RequireUserId();

            var quiz = await db.Quizzes.FirstOrDefaultAsync(q => q.Id == cmd.QuizId, ct)
                       ?? throw new NotFoundException("Quiz not found.");

            if (quiz.CreatorId != userId) throw new ForbiddenException("Only the creator can close this quiz.");
            if (quiz.ClosedAt is not null) throw new ConflictException("Quiz is already closed.");
            if (!quiz.IsActive) throw new ConflictException("Only a published quiz can be closed.");

            quiz.IsActive = false;
            quiz.ClosedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            await events.PublishAsync(new QuizClosed(quiz.Id), ct);
            return Unit.Value;
        }
    }
}
