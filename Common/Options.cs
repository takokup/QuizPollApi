namespace QuizPollApi.Common;

public sealed class JwtOptions
{
    public string Key { get; set; } = "";
    public string Issuer { get; set; } = "QuizPollApi";
    public string Audience { get; set; } = "QuizPollApi";
    public int ExpiryMinutes { get; set; } = 120;
}

public sealed class SecurityOptions
{
    public string HmacKey { get; set; } = "";

    public string? CacheEncryptionKey { get; set; }
    public bool TrustForwardedHeaders { get; set; }

    public int SessionTokenMaxAgeHours { get; set; } = 24;
}

public sealed class CaptchaOptions
{
    public bool Enabled { get; set; }
    public string VerifyUrl { get; set; } = "https://www.google.com/recaptcha/api/siteverify";
    public string Secret { get; set; } = "";
}

public sealed class RateLimitOptions
{
    public int GlobalPerIpPerMinute { get; set; } = 300;
    public int AuthPerIpPerMinute { get; set; } = 10;
    public int StartPerIpPerMinute { get; set; } = 60;
    public int SubmitPerIpPerMinute { get; set; } = 20;
    public int SubmitPerUserPerMinute { get; set; } = 20;
    public int HubJoinPerIpPerMinute { get; set; } = 60;
}

public sealed class ExportOptions
{
    public int MaxRows { get; set; } = 50000;
}
