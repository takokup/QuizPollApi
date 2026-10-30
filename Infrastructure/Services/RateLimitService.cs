using System.Collections.Concurrent;
using StackExchange.Redis;

namespace QuizPollApi.Infrastructure.Services;

public interface IRateLimitService
{
    Task CheckAsync(string scope, string identifier, int limit, TimeSpan? window = null, CancellationToken ct = default);
}
public sealed class RateLimitService(IConnectionMultiplexer redis, ILogger<RateLimitService> log) : IRateLimitService
{
    private const string Script = """
        local cur = redis.call('INCR', KEYS[1])
        if cur == 1 then redis.call('EXPIRE', KEYS[1], ARGV[1]) end
        local prev = tonumber(redis.call('GET', KEYS[2]) or '0')
        return {cur, prev}
        """;

    private static readonly ConcurrentDictionary<string, (long Window, int Count)> Local = new();

    public async Task CheckAsync(string scope, string identifier, int limit, TimeSpan? window = null, CancellationToken ct = default)
    {
        var win = window ?? TimeSpan.FromMinutes(1);
        var winSeconds = (long)win.TotalSeconds;
        var nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var index = nowUnix / winSeconds;
        var elapsed = (nowUnix % winSeconds) / (double)winSeconds;

        double estimate;
        try
        {
            var result = (RedisResult[])(await redis.GetDatabase().ScriptEvaluateAsync(
                Script,
                [(RedisKey)$"qp:rl:{scope}:{identifier}:{index}", (RedisKey)$"qp:rl:{scope}:{identifier}:{index - 1}"],
                [(RedisValue)(winSeconds * 2)]))!;
            var cur = (long)result[0];
            var prev = (long)result[1];
            estimate = prev * (1 - elapsed) + cur;
        }
        catch (RedisException ex)
        {
            log.LogWarning(ex, "Redis rate limiter unavailable, using local fallback");
            estimate = LocalIncrement($"{scope}:{identifier}", index);
        }

        if (estimate > limit)
        {
            var retry = (int)Math.Max(1, winSeconds - (nowUnix % winSeconds));
            throw new RateLimitExceededException("Too many requests. Please slow down.", retry);
        }
    }

    private static int LocalIncrement(string key, long window)
    {
        if (Local.Count > 50_000) Local.Clear(); // crude memory guard
        var entry = Local.AddOrUpdate(key,
            _ => (window, 1),
            (_, old) => old.Window == window ? (window, old.Count + 1) : (window, 1));
        return entry.Count;
    }
}
