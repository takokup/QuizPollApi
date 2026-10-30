namespace QuizPollApi.Features.Responses;

public static class SubmitResponse
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
            if (user.UserId is null && !quiz.IsAnonymousAllowed)
                throw new UnauthorizedException("This quiz requires you to sign in.");

            await rateLimiter.CheckAsync("submit-ip", user.IpHash, limits.Value.SubmitPerIpPerMinute, ct: ct);
            if (user.UserId is { } uid)
                await rateLimiter.CheckAsync("submit-user", uid.ToString(), limits.Value.SubmitPerUserPerMinute, ct: ct);

            var validated = AnswerValidator.Build(quiz, cmd.Answers);

            var response = await FindOrCreate(quiz, cmd.SessionToken, ct);
            if (response.CompletedAt is not null) throw new ConflictException("You have already submitted a response to this quiz.");

            foreach (var a in validated.Answers) response.Answers.Add(a);
            response.CompletedAt = DateTime.UtcNow;
            response.Score = validated.Score;

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (ex.IsUniqueViolation())
            {
                throw new ConflictException("A response has already been submitted.");
            }

            await events.PublishAsync(new ResponseSubmitted(quiz.Id, response.Id), ct);

            return new ResponseSubmittedDto(response.Id, validated.Score, validated.MaxScore);
        }

        private async Task<Response> FindOrCreate(QuizDefinition quiz, string? sessionToken, CancellationToken ct)
        {
            if (user.UserId is { } userId)
            {
                var existing = await db.Responses.Include(r => r.Answers)
                    .FirstOrDefaultAsync(r => r.QuizId == quiz.Id && r.UserId == userId, ct);
                if (existing is not null) return existing;

                var created = new Response { QuizId = quiz.Id, UserId = userId, IpAddressHash = user.IpHash, StartedAt = DateTime.UtcNow };
                db.Responses.Add(created);
                return created;
            }

            var sessionId = sessions.Validate(sessionToken, quiz.Id)
                            ?? throw new UnauthorizedException("Missing or invalid session token. Call /start first.");

            return await db.Responses.Include(r => r.Answers)
                       .FirstOrDefaultAsync(r => r.QuizId == quiz.Id && r.SessionId == sessionId, ct)
                   ?? throw new NotFoundException("Response session not found. Call /start first.");
        }
    }
}
