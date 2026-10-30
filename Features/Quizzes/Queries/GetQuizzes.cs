namespace QuizPollApi.Features.Quizzes;

public static class GetQuizzes
{
    public sealed record Query(
        int Page = 1, int PageSize = 20, QuizType? Type = null, bool Mine = false, string? Search = null)
        : IQuery<PagedResult<QuizSummaryDto>>;

    public sealed class Handler(ReadDbContext readDb, AppDbContext primaryDb, ICurrentUser user)
        : IQueryHandler<Query, PagedResult<QuizSummaryDto>>
    {
        public async Task<PagedResult<QuizSummaryDto>> Handle(Query query, CancellationToken ct)
        {
            var page = Math.Max(1, query.Page);
            var pageSize = Math.Clamp(query.PageSize, 1, 100);
            var now = DateTime.UtcNow;

            DbContext ctx = query.Mine ? primaryDb : readDb;
            var quizzes = ctx.Set<Quiz>().AsNoTracking();

            if (query.Mine)
            {
                var uid = user.RequireUserId();
                quizzes = quizzes.Where(q => q.CreatorId == uid);
            }
            else
            {
                quizzes = quizzes.Where(q =>
                    q.Visibility == QuizVisibility.Public && q.IsActive && q.ClosedAt == null &&
                    (q.EndsAt == null || q.EndsAt > now));
            }

            if (query.Type is { } type) quizzes = quizzes.Where(q => q.Type == type);

            if (!string.IsNullOrWhiteSpace(query.Search))
            {
                var term = query.Search.Trim();
                if (term.Length > 100) term = term[..100];
                term = term.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
                quizzes = quizzes.Where(q => EF.Functions.ILike(q.Title, $"%{term}%"));
            }

            var total = await quizzes.CountAsync(ct);

            var rows = await quizzes
                .OrderByDescending(q => q.CreatedAt).ThenByDescending(q => q.Id)
                .Skip((page - 1) * pageSize).Take(pageSize)
                .Select(q => new
                {
                    q.Id, q.Title, q.Description, q.Type, q.Visibility, q.IsActive, q.ClosedAt,
                    q.StartsAt, q.EndsAt, q.CreatedAt, q.CreatorId,
                    ResponseCount = ctx.Set<Response>().Count(r => r.QuizId == q.Id && r.CompletedAt != null)
                })
                .ToListAsync(ct);

            var items = rows.Select(r => new QuizSummaryDto(
                r.Id, r.Title, r.Description, r.Type, r.Visibility,
                QuizRules.StatusOf(r.IsActive, r.ClosedAt, r.EndsAt, now),
                r.StartsAt, r.EndsAt, r.CreatedAt, r.ResponseCount, r.CreatorId)).ToList();

            return new PagedResult<QuizSummaryDto>(items, page, pageSize, total);
        }
    }
}
