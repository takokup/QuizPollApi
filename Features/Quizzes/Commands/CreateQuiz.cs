namespace QuizPollApi.Features.Quizzes;

public static class CreateQuiz
{
    public sealed record Command(
        string Title,
        QuizType Type,
        string? Description = null,
        QuizVisibility Visibility = QuizVisibility.Public,
        bool IsAnonymousAllowed = true,
        DateTime? StartsAt = null,
        DateTime? EndsAt = null,
        ResultVisibility ResultVisibility = ResultVisibility.Public,
        List<QuestionInput>? Questions = null) : ICommand<QuizCreatedDto>;

    public sealed class Handler(AppDbContext db, ICurrentUser user) : ICommandHandler<Command, QuizCreatedDto>
    {
        public async Task<QuizCreatedDto> Handle(Command cmd, CancellationToken ct)
        {
            var creatorId = user.RequireUserId();

            var title = (cmd.Title ?? "").Trim();
            Guard.That(title.Length is >= 1 and <= 200, "Title must be 1-200 characters.");
            Guard.That(cmd.Description is null || cmd.Description.Length <= 2000, "Description must be at most 2000 characters.");
            Guard.That(Enum.IsDefined(cmd.Type) && Enum.IsDefined(cmd.Visibility) && Enum.IsDefined(cmd.ResultVisibility), "Invalid enum value.");

            var startsAt = cmd.StartsAt.ToUtc();
            var endsAt = cmd.EndsAt.ToUtc();
            Guard.That(endsAt is null || endsAt > DateTime.UtcNow, "EndsAt must be in the future.");
            Guard.That(startsAt is null || endsAt is null || startsAt < endsAt, "StartsAt must be before EndsAt.");

            var questions = cmd.Questions ?? [];
            Guard.That(questions.Count <= QuestionRules.MaxQuestionsPerQuiz, $"At most {QuestionRules.MaxQuestionsPerQuiz} questions per quiz.");
            foreach (var q in questions) QuestionRules.Validate(q, cmd.Type);

            var quiz = new Quiz
            {
                CreatorId = creatorId,
                Title = title,
                Description = cmd.Description?.Trim(),
                Type = cmd.Type,
                Visibility = cmd.Visibility,
                AccessCode = cmd.Visibility == QuizVisibility.Private ? Helpers.GenerateAccessCode() : null,
                IsAnonymousAllowed = cmd.IsAnonymousAllowed,
                StartsAt = startsAt,
                EndsAt = endsAt,
                ResultVisibility = cmd.ResultVisibility,
                IsActive = false,
                Questions = questions.Select((q, i) => QuestionRules.Build(q, i)).ToList()
            };

            db.Quizzes.Add(quiz);
            await db.SaveChangesAsync(ct);
            return new QuizCreatedDto(quiz.Id, quiz.AccessCode);
        }
    }
}
