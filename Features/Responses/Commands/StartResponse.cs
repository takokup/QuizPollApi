namespace QuizPollApi.Features.Responses;

public static class StartResponse
{
    public sealed record Command(int QuizId, string? AccessCode, string? CaptchaToken) : ICommand<StartResponseResult>;

    public sealed class Handler(
        AppDbContext db, IQuizAccess access, ICurrentUser user, ISessionTokenService sessions,
        IRateLimitService rateLimiter, ICaptchaService captcha, IOptions<RateLimitOptions> limits)
        : ICommandHandler<Command, StartResponseResult>
    {
        public async Task<StartResponseResult> Handle(Command cmd, CancellationToken ct)
        {
            await rateLimiter.CheckAsync("start", user.IpHash, limits.Value.StartPerIpPerMinute, ct: ct);

            var quiz = await access.GetViewableAsync(cmd.QuizId, cmd.AccessCode, ct);
            if (!quiz.IsOpen(DateTime.UtcNow)) throw new ConflictException("This quiz is not accepting responses.");

            if (user.UserId is { } userId)
            {
                var existing = await db.Responses.AsNoTracking()
                    .FirstOrDefaultAsync(r => r.QuizId == quiz.Id && r.UserId == userId, ct);
                if (existing?.CompletedAt is not null) throw new ConflictException("You have already responded to this quiz.");
                if (existing is not null) return new StartResponseResult(existing.Id, null, existing.StartedAt);

                return await Create(quiz, userId, null, ct);
            }

            if (!quiz.IsAnonymousAllowed) throw new UnauthorizedException("This quiz requires you to sign in.");

            if (quiz.Visibility == QuizVisibility.Public && !await captcha.VerifyAsync(cmd.CaptchaToken, ct))
                throw new ForbiddenException("CAPTCHA verification failed.");

            var ipHash = user.IpHash;
            var current = await db.Responses.AsNoTracking()
                .FirstOrDefaultAsync(r => r.QuizId == quiz.Id && r.UserId == null && r.IpAddressHash == ipHash, ct);

            if (current is not null)
            {
                if (current.CompletedAt is not null)
                    throw new ConflictException("A response from this network has already been submitted.");
                return new StartResponseResult(current.Id, sessions.Create(current.SessionId!, quiz.Id), current.StartedAt);
            }

            var sessionId = Guid.NewGuid().ToString("N");
            var result = await Create(quiz, null, sessionId, ct);
            return result with { SessionToken = sessions.Create(sessionId, quiz.Id) };
        }

        private async Task<StartResponseResult> Create(QuizDefinition quiz, int? userId, string? sessionId, CancellationToken ct)
        {
            var response = new Response
            {
                QuizId = quiz.Id,
                UserId = userId,
                SessionId = sessionId,
                IpAddressHash = user.IpHash,
                StartedAt = DateTime.UtcNow
            };
            db.Responses.Add(response);

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (ex.IsUniqueViolation())
            {
                throw new ConflictException("You have already started or submitted a response to this quiz.");
            }

            return new StartResponseResult(response.Id, null, response.StartedAt);
        }
    }
}
