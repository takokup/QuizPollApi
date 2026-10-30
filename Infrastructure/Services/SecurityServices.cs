using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace QuizPollApi.Infrastructure.Services;

public interface IIpHasher
{
    string Hash(IPAddress? ip);
}

public sealed class IpHasher(IOptions<SecurityOptions> options) : IIpHasher
{
    private readonly byte[] _key = Encoding.UTF8.GetBytes(options.Value.HmacKey);

    public string Hash(IPAddress? ip)
    {
        if (ip is null) return "unknown";
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();

        var bytes = ip.GetAddressBytes();
        if (bytes.Length == 16) Array.Clear(bytes, 8, 8);

        return Convert.ToHexString(HMACSHA256.HashData(_key, bytes)).ToLowerInvariant();
    }
}

public interface ICurrentUser
{
    int? UserId { get; }
    bool IsAuthenticated { get; }
    string IpHash { get; }
}

public sealed class CurrentUser(IHttpContextAccessor accessor, IIpHasher hasher) : ICurrentUser
{
    private string? _ipHash;

    public int? UserId => accessor.HttpContext?.User.UserId();
    public bool IsAuthenticated => UserId is not null;
    public string IpHash => _ipHash ??= hasher.Hash(accessor.HttpContext?.Connection.RemoteIpAddress);
}

public static class CurrentUserExtensions
{
    public static int RequireUserId(this ICurrentUser user) =>
        user.UserId ?? throw new UnauthorizedException("Authentication required.");
}

public interface ISessionTokenService
{
    string Create(string sessionId, int quizId);

    /// <summary>Returns the session id when the token is authentic, belongs to the quiz and is not too old.</summary>
    string? Validate(string? token, int quizId);
}

public sealed class SessionTokenService(IOptions<SecurityOptions> options) : ISessionTokenService
{
    private readonly byte[] _key = Encoding.UTF8.GetBytes(options.Value.HmacKey);
    private readonly TimeSpan _maxAge = TimeSpan.FromHours(options.Value.SessionTokenMaxAgeHours);

    public string Create(string sessionId, int quizId)
    {
        var issued = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return $"{sessionId}.{quizId}.{issued}.{Sign(sessionId, quizId, issued)}";
    }

    public string? Validate(string? token, int quizId)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 256) return null;
        var parts = token.Split('.');
        if (parts.Length != 4) return null;
        if (!int.TryParse(parts[1], out var tokenQuiz) || tokenQuiz != quizId) return null;
        if (!long.TryParse(parts[2], out var issued)) return null;

        var expected = Sign(parts[0], quizId, issued);
        if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(parts[3])))
            return null;

        var age = DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds(issued);
        return age <= _maxAge && age >= TimeSpan.FromMinutes(-5) ? parts[0] : null;
    }

    private string Sign(string sessionId, int quizId, long issued) =>
        Base64Url.Encode(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes($"v1|{sessionId}|{quizId}|{issued}")));
}

internal static class Base64Url
{
    public static string Encode(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

public interface ICaptchaService
{
    Task<bool> VerifyAsync(string? token, CancellationToken ct);
}

public sealed class CaptchaService(HttpClient http, IOptions<CaptchaOptions> options, ILogger<CaptchaService> log) : ICaptchaService
{
    public async Task<bool> VerifyAsync(string? token, CancellationToken ct)
    {
        var o = options.Value;
        if (!o.Enabled) return true;
        if (string.IsNullOrWhiteSpace(token) || token.Length > 4096) return false;

        try
        {
            using var response = await http.PostAsync(o.VerifyUrl, new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["secret"] = o.Secret,
                ["response"] = token
            }), ct);

            if (!response.IsSuccessStatusCode) return false;

            using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            return doc.RootElement.TryGetProperty("success", out var success) && success.GetBoolean();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            log.LogWarning(ex, "CAPTCHA verification failed due to a provider error");
            return false; // fail closed
        }
    }
}

public interface IPasswordService
{
    string Hash(string password);
    bool Verify(string password, string stored);
}

public sealed class PasswordService : IPasswordService
{
    private const int Iterations = 210_000;

    public string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);
        return $"v1.{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    public bool Verify(string password, string stored)
    {
        var parts = stored.Split('.');
        if (parts.Length != 4 || parts[0] != "v1" || !int.TryParse(parts[1], out var iterations)) return false;
        try
        {
            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

public interface IJwtTokenService
{
    (string Token, DateTime ExpiresAt) Create(User user);
}

public sealed class JwtTokenService(IOptions<JwtOptions> options) : IJwtTokenService
{
    public (string Token, DateTime ExpiresAt) Create(User user)
    {
        var o = options.Value;
        var expires = DateTime.UtcNow.AddMinutes(o.ExpiryMinutes);
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity([
                new Claim("sub", user.Id.ToString()),
                new Claim("name", user.DisplayName)
            ]),
            Issuer = o.Issuer,
            Audience = o.Audience,
            Expires = expires,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(o.Key)), SecurityAlgorithms.HmacSha256)
        };
        return (new JsonWebTokenHandler().CreateToken(descriptor), expires);
    }
}
