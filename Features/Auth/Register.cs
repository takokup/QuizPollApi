using System.Text.RegularExpressions;

namespace QuizPollApi.Features.Auth;

public static partial class Register
{
    private static readonly HashSet<string> AgeGroups = ["under-18", "18-24", "25-34", "35-44", "45-54", "55+"];

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailPattern();

    public sealed record Command(
        string Email, string Password, string DisplayName,
        string? Country = null, string? AgeGroup = null, string? Gender = null) : ICommand<AuthResultDto>;

    public sealed class Handler(
        AppDbContext db, IPasswordService passwords, IJwtTokenService jwt,
        IRateLimitService rateLimiter, ICurrentUser user, IOptions<RateLimitOptions> limits)
        : ICommandHandler<Command, AuthResultDto>
    {
        public async Task<AuthResultDto> Handle(Command cmd, CancellationToken ct)
        {
            await rateLimiter.CheckAsync("auth", user.IpHash, limits.Value.AuthPerIpPerMinute, ct: ct);

            var email = (cmd.Email ?? "").Trim().ToLowerInvariant();
            Guard.That(email.Length <= 254 && EmailPattern().IsMatch(email), "A valid email is required.");
            Guard.That(cmd.Password is { Length: >= 8 and <= 128 }, "Password must be 8-128 characters.");
            var name = (cmd.DisplayName ?? "").Trim();
            Guard.That(name.Length is >= 1 and <= 100, "DisplayName must be 1-100 characters.");

            var country = string.IsNullOrWhiteSpace(cmd.Country) ? null : cmd.Country.Trim().ToUpperInvariant();
            Guard.That(country is null || (country.Length == 2 && country.All(char.IsAsciiLetterUpper)), "Country must be an ISO 3166 alpha-2 code.");
            Guard.That(cmd.AgeGroup is null || AgeGroups.Contains(cmd.AgeGroup), $"AgeGroup must be one of: {string.Join(", ", AgeGroups)}.");
            Guard.That(cmd.Gender is null || cmd.Gender.Trim().Length <= 30, "Gender must be at most 30 characters.");

            if (await db.Users.AnyAsync(u => u.Email == email, ct))
                throw new ConflictException("Email is already registered.");

            var entity = new User
            {
                Email = email,
                PasswordHash = passwords.Hash(cmd.Password),
                DisplayName = name,
                Country = country,
                AgeGroup = cmd.AgeGroup,
                Gender = string.IsNullOrWhiteSpace(cmd.Gender) ? null : cmd.Gender.Trim()
            };
            db.Users.Add(entity);

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (ex.IsUniqueViolation())
            {
                throw new ConflictException("Email is already registered.");
            }

            var (token, expires) = jwt.Create(entity);
            return new AuthResultDto(token, expires, entity.Id, entity.DisplayName);
        }
    }
}
