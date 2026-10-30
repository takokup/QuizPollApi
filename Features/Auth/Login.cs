namespace QuizPollApi.Features.Auth;

public static class Login
{
    public sealed record Command(string Email, string Password) : ICommand<AuthResultDto>;

    public sealed class Handler(
        AppDbContext db, IPasswordService passwords, IJwtTokenService jwt,
        IRateLimitService rateLimiter, ICurrentUser user, IOptions<RateLimitOptions> limits)
        : ICommandHandler<Command, AuthResultDto>
    {
        // verified against when the account does not exist, so response time does not reveal valid emails
        private static readonly string DummyHash = new PasswordService().Hash("not-a-real-password");

        public async Task<AuthResultDto> Handle(Command cmd, CancellationToken ct)
        {
            await rateLimiter.CheckAsync("auth", user.IpHash, limits.Value.AuthPerIpPerMinute, ct: ct);

            var email = (cmd.Email ?? "").Trim().ToLowerInvariant();
            var account = email.Length is > 0 and <= 254
                ? await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Email == email, ct)
                : null;

            var ok = passwords.Verify(cmd.Password ?? "", account?.PasswordHash ?? DummyHash);
            if (account is null || !ok)
                throw new UnauthorizedException("Invalid email or password.");

            var (token, expires) = jwt.Create(account);
            return new AuthResultDto(token, expires, account.Id, account.DisplayName);
        }
    }
}
