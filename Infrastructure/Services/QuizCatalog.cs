namespace QuizPollApi.Infrastructure.Services;

public sealed record OptionDef(int Id, string Text, int OrderIndex, bool IsCorrect);

public sealed record QuestionDef(
    int Id, string Text, QuestionType Type, int OrderIndex, bool IsRequired,
    int? TimeLimitSeconds, string? ImageUrl, int MaxRating, List<OptionDef> Options);

public sealed record QuizDefinition(
    int Id, int CreatorId, string Title, string? Description, QuizType Type, QuizVisibility Visibility,
    string? AccessCode, bool IsAnonymousAllowed, DateTime? StartsAt, DateTime? EndsAt, bool IsActive,
    DateTime? ClosedAt, ResultVisibility ResultVisibility, DateTime CreatedAt, List<QuestionDef> Questions)
{
    public bool IsDraft => !IsActive && ClosedAt is null;

    public bool IsOpen(DateTime now) =>
        IsActive && ClosedAt is null && (StartsAt is null || now >= StartsAt) && (EndsAt is null || now < EndsAt);

    public bool IsClosed(DateTime now) => ClosedAt is not null || (IsActive && EndsAt is not null && now >= EndsAt);

    public QuizStatus Status(DateTime now) => QuizRules.StatusOf(IsActive, ClosedAt, EndsAt, now);
}

public static class QuizRules
{
    public static QuizStatus StatusOf(bool isActive, DateTime? closedAt, DateTime? endsAt, DateTime now) =>
        closedAt is not null || (isActive && endsAt is not null && now >= endsAt) ? QuizStatus.Closed
        : isActive ? QuizStatus.Active
        : QuizStatus.Draft;
}

public interface IQuizCatalog
{
    Task<QuizDefinition?> GetAsync(int quizId, CancellationToken ct = default);
    Task InvalidateAsync(int quizId, CancellationToken ct = default);
}

public sealed class QuizCatalog(AppDbContext db, ICacheService cache) : IQuizCatalog
{
    public Task<QuizDefinition?> GetAsync(int quizId, CancellationToken ct = default) =>
        cache.GetOrCreateAsync(
            CacheKeys.QuizDefinition(quizId),
            () => Load(quizId, ct),
            TimeSpan.FromMinutes(30),
            TimeSpan.FromMinutes(10),
            ct);

    public Task InvalidateAsync(int quizId, CancellationToken ct = default) =>
        cache.RemoveAsync(CacheKeys.QuizDefinition(quizId), ct);

    private async Task<QuizDefinition?> Load(int quizId, CancellationToken ct)
    {
        var q = await db.Quizzes.AsNoTracking()
            .AsSplitQuery()
            .Include(x => x.Questions.OrderBy(y => y.OrderIndex))
            .ThenInclude(y => y.Options.OrderBy(o => o.OrderIndex))
            .FirstOrDefaultAsync(x => x.Id == quizId, ct);

        if (q is null) return null;

        return new QuizDefinition(
            q.Id, q.CreatorId, q.Title, q.Description, q.Type, q.Visibility, q.AccessCode, q.IsAnonymousAllowed,
            q.StartsAt.ToUtc(), q.EndsAt.ToUtc(), q.IsActive, q.ClosedAt.ToUtc(), q.ResultVisibility, q.CreatedAt,
            q.Questions.Select(x => new QuestionDef(
                x.Id, x.QuestionText, x.QuestionType, x.OrderIndex, x.IsRequired, x.TimeLimitSeconds, x.ImageUrl, x.MaxRating,
                x.Options.Select(o => new OptionDef(o.Id, o.OptionText, o.OrderIndex, o.IsCorrect)).ToList())).ToList());
    }
}
