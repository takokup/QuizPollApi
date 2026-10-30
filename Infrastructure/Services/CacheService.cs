using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using StackExchange.Redis;

namespace QuizPollApi.Infrastructure.Services;

public interface ICacheService
{
    Task<T?> GetAsync<T>(string key, CancellationToken ct = default) where T : class;
    Task SetAsync<T>(string key, T value, TimeSpan absoluteExpiration, TimeSpan? slidingExpiration = null, CancellationToken ct = default) where T : class;
    Task RemoveAsync(string key, CancellationToken ct = default);

    Task<T?> GetOrCreateAsync<T>(string key, Func<Task<T?>> factory, TimeSpan absoluteExpiration,
        TimeSpan? slidingExpiration = null, CancellationToken ct = default) where T : class;
}

public sealed class CacheService : ICacheService
{
    private const string Prefix = "qp:";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly IConnectionMultiplexer _redis;
    private readonly KeyedLock _locks;
    private readonly ILogger<CacheService> _log;
    private readonly byte[]? _encKey;

    public CacheService(IConnectionMultiplexer redis, IOptions<SecurityOptions> security, KeyedLock locks, ILogger<CacheService> log)
    {
        _redis = redis;
        _locks = locks;
        _log = log;
        var k = security.Value.CacheEncryptionKey;
        if (!string.IsNullOrWhiteSpace(k))
        {
            _encKey = Convert.FromBase64String(k);
            if (_encKey.Length != 32)
                throw new InvalidOperationException("Security:CacheEncryptionKey must be a base64-encoded 32-byte key.");
        }
    }

    public Task<T?> GetAsync<T>(string key, CancellationToken ct = default) where T : class => TryGet<T>(key, null);

    public async Task SetAsync<T>(string key, T value, TimeSpan absoluteExpiration, TimeSpan? slidingExpiration = null,
        CancellationToken ct = default) where T : class
    {
        try
        {
            var ttl = slidingExpiration is { } s && s < absoluteExpiration ? s : absoluteExpiration;
            var expiryMs = DateTimeOffset.UtcNow.Add(absoluteExpiration).ToUnixTimeMilliseconds();
            await _redis.GetDatabase().StringSetAsync(Prefix + key, Encode(key, value, expiryMs), ttl);
        }
        catch (RedisException ex)
        {
            _log.LogWarning(ex, "Cache write failed for {Key}", key);
        }
    }

    public async Task RemoveAsync(string key, CancellationToken ct = default)
    {
        try
        {
            await _redis.GetDatabase().KeyDeleteAsync(Prefix + key);
        }
        catch (RedisException ex)
        {
            _log.LogWarning(ex, "Cache delete failed for {Key}", key);
        }
    }

    public async Task<T?> GetOrCreateAsync<T>(string key, Func<Task<T?>> factory, TimeSpan absoluteExpiration,
        TimeSpan? slidingExpiration = null, CancellationToken ct = default) where T : class
    {
        var cached = await TryGet<T>(key, slidingExpiration);
        if (cached is not null) return cached;

        using var _ = await _locks.AcquireAsync(key, ct);

        cached = await TryGet<T>(key, slidingExpiration); // somebody may have filled it while we waited
        if (cached is not null) return cached;

        var value = await factory();
        if (value is not null)
            await SetAsync(key, value, absoluteExpiration, slidingExpiration, ct);
        return value;
    }

    private async Task<T?> TryGet<T>(string key, TimeSpan? sliding) where T : class
    {
        try
        {
            var db = _redis.GetDatabase();
            var raw = (byte[]?)await db.StringGetAsync(Prefix + key);
            if (raw is null) return null;

            var plain = Decrypt(key, raw);
            if (plain is null || plain.Length < 8) return null;

            var expiryMs = BinaryPrimitives.ReadInt64LittleEndian(plain);
            var remaining = DateTimeOffset.FromUnixTimeMilliseconds(expiryMs) - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                await db.KeyDeleteAsync(Prefix + key, CommandFlags.FireAndForget);
                return null;
            }

            if (sliding is { } s)
                await db.KeyExpireAsync(Prefix + key, s < remaining ? s : remaining, CommandFlags.FireAndForget);

            return JsonSerializer.Deserialize<T>(plain.AsSpan(8), Json);
        }
        catch (Exception ex) when (ex is RedisException or JsonException or CryptographicException)
        {
            _log.LogWarning(ex, "Cache read failed for {Key}", key);
            return null;
        }
    }

    private byte[] Encode<T>(string key, T value, long expiryMs)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(value, Json);
        var plain = new byte[8 + json.Length];
        BinaryPrimitives.WriteInt64LittleEndian(plain, expiryMs);
        json.CopyTo(plain, 8);
        return Encrypt(key, plain);
    }

    private byte[] Encrypt(string key, byte[] plain)
    {
        if (_encKey is null) return plain;

        var nonce = RandomNumberGenerator.GetBytes(12);
        var tag = new byte[16];
        var cipher = new byte[plain.Length];
        using var aes = new AesGcm(_encKey, 16);
        aes.Encrypt(nonce, plain, cipher, tag, System.Text.Encoding.UTF8.GetBytes(key));

        var result = new byte[12 + 16 + cipher.Length];
        nonce.CopyTo(result, 0);
        tag.CopyTo(result, 12);
        cipher.CopyTo(result, 28);
        return result;
    }

    private byte[]? Decrypt(string key, byte[] data)
    {
        if (_encKey is null) return data;
        if (data.Length < 28) return null;

        var plain = new byte[data.Length - 28];
        using var aes = new AesGcm(_encKey, 16);
        aes.Decrypt(data.AsSpan(0, 12), data.AsSpan(28), data.AsSpan(12, 16), plain, System.Text.Encoding.UTF8.GetBytes(key));
        return plain;
    }
}
