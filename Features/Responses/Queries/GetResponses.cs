namespace QuizPollApi.Features.Responses;

public static class GetResponses
{
    public sealed record Query(int QuizId, int Page = 1, int PageSize = 50, bool IncludeAnswers = false)
        : IQuery<PagedResult<ResponseDto>>;

    public sealed class Handler(ReadDbContext readDb, IQuizCatalog catalog, ICurrentUser user)
        : IQueryHandler<Query, PagedResult<ResponseDto>>
    {
        public async Task<PagedResult<ResponseDto>> Handle(Query query, CancellationToken ct)
        {
            var userId = user.RequireUserId();
            var quiz = await catalog.GetAsync(query.QuizId, ct) ?? throw new NotFoundException("Quiz not found.");
            if (quiz.CreatorId != userId) throw new ForbiddenException("Only the creator can list responses.");

            var page = Math.Max(1, query.Page);
            var pageSize = Math.Clamp(query.PageSize, 1, 200);

            var baseQuery = readDb.Responses.AsNoTracking().Where(r => r.QuizId == quiz.Id && r.CompletedAt != null);
            var total = await baseQuery.CountAsync(ct);

            var rows = await baseQuery
                .OrderByDescending(r => r.CompletedAt).ThenByDescending(r => r.Id)
                .Skip((page - 1) * pageSize).Take(pageSize)
                .Select(r => new { r.Id, r.UserId, r.StartedAt, r.CompletedAt, r.Score })
                .ToListAsync(ct);

            var answers = new Dictionary<int, List<AnswerDto>>();
            if (query.IncludeAnswers && rows.Count > 0)
            {
                var ids = rows.Select(r => r.Id).ToList();
                var loaded = await readDb.ResponseAnswers.AsNoTracking()
                    .Where(a => ids.Contains(a.ResponseId))
                    .OrderBy(a => a.QuestionId)
                    .Select(a => new { a.ResponseId, Answer = new AnswerDto(a.QuestionId, a.SelectedOptionIds, a.TextAnswer, a.RatingValue) })
                    .ToListAsync(ct);
                answers = loaded.GroupBy(x => x.ResponseId).ToDictionary(g => g.Key, g => g.Select(x => x.Answer).ToList());
            }

            var items = rows.Select(r => new ResponseDto(
                r.Id, quiz.Id, r.UserId, r.StartedAt, r.CompletedAt, r.Score,
                answers.GetValueOrDefault(r.Id) ?? [])).ToList();
            return new PagedResult<ResponseDto>(items, page, pageSize, total);
        }
    }
}
