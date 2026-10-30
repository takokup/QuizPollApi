using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;

namespace QuizPollApi.Common;

public static class Helpers
{
    public static DateTime ToUtc(this DateTime d) => d.Kind switch
    {
        DateTimeKind.Utc => d,
        DateTimeKind.Local => d.ToUniversalTime(),
        _ => DateTime.SpecifyKind(d, DateTimeKind.Utc)
    };

    public static DateTime? ToUtc(this DateTime? d) => d?.ToUtc();

    public static string GenerateAccessCode(int length = 8)
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // no 0/O/1/I
        return string.Create(length, alphabet, static (span, a) =>
        {
            for (var i = 0; i < span.Length; i++)
                span[i] = a[RandomNumberGenerator.GetInt32(a.Length)];
        });
    }

    public static bool IsUniqueViolation(this DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    public static int? UserId(this System.Security.Claims.ClaimsPrincipal? principal) =>
        int.TryParse(principal?.FindFirst("sub")?.Value, out var id) ? id : null;

    public static bool CodesMatch(string? expected, string? provided)
    {
        if (string.IsNullOrEmpty(expected) || string.IsNullOrEmpty(provided)) return false;
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected.Trim().ToUpperInvariant()),
            Encoding.UTF8.GetBytes(provided.Trim().ToUpperInvariant()));
    }
}

public sealed class KeyedLock
{
    private readonly SemaphoreSlim[] _stripes = Enumerable.Range(0, 64).Select(_ => new SemaphoreSlim(1, 1)).ToArray();

    public async Task<IDisposable> AcquireAsync(string key, CancellationToken ct = default)
    {
        var stripe = _stripes[(int)((uint)key.GetHashCode() % (uint)_stripes.Length)];
        await stripe.WaitAsync(ct);
        return new Releaser(stripe);
    }

    private sealed class Releaser(SemaphoreSlim semaphore) : IDisposable
    {
        public void Dispose() => semaphore.Release();
    }
}
public static class Sql
{
    public static async Task<List<T>> QueryAsync<T>(
        DbContext db, string sql, Func<DbDataReader, T> map, CancellationToken ct,
        params (string Name, object? Value)[] args)
    {
        var conn = db.Database.GetDbConnection();
        var wasClosed = conn.State != ConnectionState.Open;
        if (wasClosed) await conn.OpenAsync(ct);
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            foreach (var (name, value) in args)
            {
                var p = cmd.CreateParameter();
                p.ParameterName = name;
                p.Value = value ?? DBNull.Value;
                cmd.Parameters.Add(p);
            }

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            var list = new List<T>();
            while (await reader.ReadAsync(ct))
                list.Add(map(reader));
            return list;
        }
        finally
        {
            if (wasClosed) await conn.CloseAsync();
        }
    }
}
