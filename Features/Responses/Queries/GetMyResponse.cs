namespace QuizPollApi.Features.Responses;

public static class GetMyResponse
{
    public sealed record Query(int QuizId, string? SessionToken) : IQuery<ResponseDto>;

    public sealed class Handler(AppDbContext db, ICurrentUser user, ISessionTokenService sessions)
        : IQueryHandler<Query, ResponseDto>
    {
        public async Task<ResponseDto> Handle(Query query, CancellationToken ct)
        {
            IQueryable<Response> responses = db.Responses.AsNoTracking().Include(r => r.Answers).Where(r => r.QuizId == query.QuizId);

            if (user.UserId is { } uid)
            {
                responses = responses.Where(r => r.UserId == uid);
            }
            else
            {
                var sessionId = sessions.Validate(query.SessionToken, query.QuizId);
                if (sessionId is not null)
                {
                    responses = responses.Where(r => r.SessionId == sessionId);
                }
                else
                {
                    var ipHash = user.IpHash;
                    responses = responses.Where(r => r.UserId == null && r.IpAddressHash == ipHash);
                }
            }

            var response = await responses.FirstOrDefaultAsync(ct) ?? throw new NotFoundException("You have no response to this quiz.");

            return new ResponseDto(
                response.Id, response.QuizId, response.UserId, response.StartedAt, response.CompletedAt, response.Score,
                response.Answers.Select(a => new AnswerDto(a.QuestionId, a.SelectedOptionIds, a.TextAnswer, a.RatingValue)).ToList());
        }
    }
}
