using System.Text.Json.Serialization;
using Microsoft.AspNetCore.HttpOverrides;
using QuizPollApi.Extensions;
using QuizPollApi.Infrastructure.SignalR;
using StackExchange.Redis;
using ExportOptions = QuizPollApi.Common.ExportOptions;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

builder.Services.Configure<JwtOptions>(config.GetSection("Jwt"));
builder.Services.Configure<SecurityOptions>(config.GetSection("Security"));
builder.Services.Configure<CaptchaOptions>(config.GetSection("Captcha"));
builder.Services.Configure<RateLimitOptions>(config.GetSection("RateLimiting"));
builder.Services.Configure<ExportOptions>(config.GetSection("Export"));

var jwt = config.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
var security = config.GetSection("Security").Get<SecurityOptions>() ?? new SecurityOptions();

if (jwt.Key.Length < 32 || security.HmacKey.Length < 32)
    throw new InvalidOperationException("Jwt:Key and Security:HmacKey must be at least 32 characters.");
if (!builder.Environment.IsDevelopment() && (jwt.Key.Contains("DEV-ONLY") || security.HmacKey.Contains("DEV-ONLY")))
    throw new InvalidOperationException("Replace the DEV-ONLY secrets (Jwt__Key, Security__HmacKey) outside Development.");

builder.WebHost.ConfigureKestrel(o =>
{
    o.AddServerHeader = false;
    o.Limits.MaxRequestBodySize = 256 * 1024;                      
    o.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(15);     
    o.Limits.MinRequestBodyDataRate = new Microsoft.AspNetCore.Server.Kestrel.Core.MinDataRate(240, TimeSpan.FromSeconds(10));
    o.Limits.MaxRequestHeadersTotalSize = 16 * 1024;
});

builder.Services.AddQuizInfrastructure(config);
builder.Services.AddCqrs();
builder.Services.AddQuizAuthentication(jwt);
builder.Services.AddForwardedHeadersIfTrusted(security);
builder.Services.AddQuizSwagger();
builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var app = builder.Build();

if (config.GetValue<bool>("Database:AutoCreate"))
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    for (var attempt = 1; ; attempt++)
    {
        try
        {
            await db.Database.EnsureCreatedAsync();
            break;
        }
        catch (Exception ex) when (attempt < 30)
        {
            logger.LogWarning("Database not ready (attempt {Attempt}/30): {Message}", attempt, ex.Message);
            await Task.Delay(TimeSpan.FromSeconds(2));
        }
    }
}

app.UseMiddleware<ExceptionMiddleware>();
if (security.TrustForwardedHeaders) app.UseForwardedHeaders();

app.UseSwagger();
app.UseSwaggerUI();

app.UseAuthentication();
app.UseMiddleware<GlobalRateLimitMiddleware>();
app.UseAuthorization();

app.MapControllers();
app.MapHub<QuizHub>("/hubs/quiz");

app.MapGet("/health", async (AppDbContext db, IConnectionMultiplexer redis, CancellationToken ct) =>
{
    var dbOk = await db.Database.CanConnectAsync(ct);
    var redisOk = redis.IsConnected;
    return dbOk && redisOk
        ? Results.Ok(new { status = "healthy", database = dbOk, redis = redisOk })
        : Results.Json(new { status = "unhealthy", database = dbOk, redis = redisOk }, statusCode: StatusCodes.Status503ServiceUnavailable);
});

app.Run();

public partial class Program;
