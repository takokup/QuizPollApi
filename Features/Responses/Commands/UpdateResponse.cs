namespace QuizPollApi.Features.Responses;

public static class UpdateResponse
{
    public sealed record Command(int QuizId, List<AnswerDto> Answers, string? SessionToken, string? AccessCode)
        : ICommand<ResponseSubmittedDto>;

    public sealed class Handler(
        AppDbContext db, IQuizAccess access, ICurrentUser user, ISessionTokenService sessions,
        IRateLimitService rateLimiter, IEventPublisher events, IOptions<RateLimitOptions> limits)
        : ICommandHandler<Command, ResponseSubmittedDto>
    {
        public async Task<ResponseSubmittedDto> Handle(Command cmd, CancellationToken ct)
        {
            var quiz = await access.GetViewableAsync(cmd.QuizId, cmd.AccessCode, ct);
            if (!quiz.IsOpen(DateTime.UtcNow)) throw new ConflictException("This quiz is not accepting responses.");
            if (quiz.Type == QuizType.Quiz) throw new ConflictException("Answers to a quiz cannot be changed after submitting.");

            await rateLimiter.CheckAsync("submit-ip", user.IpHash, limits.Value.SubmitPerIpPerMinute, ct: ct);

            var validated = AnswerValidator.Build(quiz, cmd.Answers);

            IQueryable<Response> responses = db.Responses.Include(r => r.Answers).Where(r => r.QuizId == quiz.Id);
            if (user.UserId is { } uid)
            {
                responses = responses.Where(r => r.UserId == uid);
            }
            else
            {
                var sessionId = sessions.Validate(cmd.SessionToken, quiz.Id)
                                ?? throw new UnauthorizedException("Missing or invalid session token.");
                responses = responses.Where(r => r.SessionId == sessionId);
            }

            var response = await responses.FirstOrDefaultAsync(ct) ?? throw new NotFoundException("No response to update.");
            if (response.CompletedAt is null) throw new ConflictException("Submit the response first.");

            var existing = response.Answers.ToDictionary(a => a.QuestionId);
            foreach (var incoming in validated.Answers)
            {
                if (existing.Remove(incoming.QuestionId, out var current))
                {
                    current.SelectedOptionIds = incoming.SelectedOptionIds;
                    current.TextAnswer = incoming.TextAnswer;
                    current.RatingValue = incoming.RatingValue;
                    current.AnsweredAt = incoming.AnsweredAt;
                }
                else
                {
                    response.Answers.Add(incoming);
                }
            }
            foreach (var removed in existing.Values) db.ResponseAnswers.Remove(removed); // answers that are no longer sent

            response.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct); // xmin token: concurrent edits of the same response -> 409

            await events.PublishAsync(new ResponseUpdated(quiz.Id, response.Id), ct);
            return new ResponseSubmittedDto(response.Id, validated.Score, validated.MaxScore);
        }
    }
}
