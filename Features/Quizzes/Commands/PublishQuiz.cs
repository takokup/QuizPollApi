namespace QuizPollApi.Features.Quizzes;

public static class PublishQuiz
{
    public sealed record Command(int QuizId) : ICommand<Unit>;

    public sealed class Handler(AppDbContext db, ICurrentUser user, IEventPublisher events)
        : ICommandHandler<Command, Unit>
    {
        public async Task<Unit> Handle(Command cmd, CancellationToken ct)
        {
            var userId = user.RequireUserId();

            var quiz = await db.Quizzes
                .Include(q => q.Questions).ThenInclude(q => q.Options)
                .FirstOrDefaultAsync(q => q.Id == cmd.QuizId, ct)
                ?? throw new NotFoundException("Quiz not found.");

            if (quiz.CreatorId != userId) throw new ForbiddenException("Only the creator can publish this quiz.");
            if (quiz.ClosedAt is not null) throw new ConflictException("A closed quiz cannot be published again.");
            if (quiz.IsActive) throw new ConflictException("Quiz is already published.");
            Guard.That(quiz.EndsAt is null || quiz.EndsAt > DateTime.UtcNow, "EndsAt is in the past; update the schedule first.");

            QuestionRules.ValidateForPublish(quiz);

            quiz.IsActive = true;
            await db.SaveChangesAsync(ct); // xmin concurrency token: a concurrent publish/close fails with 409

            await events.PublishAsync(new QuizPublished(quiz.Id), ct);
            return Unit.Value;
        }
    }
}
