using System.Collections.Concurrent;
using StackExchange.Redis;

namespace QuizPollApi.Infrastructure.Services;

public abstract class DirtyQuizTracker
{
    private readonly ConcurrentDictionary<int, byte> _dirty = new();

    public void Mark(int quizId) => _dirty[quizId] = 0;

    public HashSet<int> Drain()
    {
        var result = new HashSet<int>();
        foreach (var id in _dirty.Keys)
            if (_dirty.TryRemove(id, out _))
                result.Add(id);
        return result;
    }
}

public sealed class LiveResultsQueue : DirtyQuizTracker;

public sealed class ParticipantsQueue : DirtyQuizTracker;

public sealed class AnalyticsQueue : DirtyQuizTracker;

public interface IParticipantTracker
{
    Task JoinAsync(int quizId, string connectionId);
    Task LeaveAsync(int quizId, string connectionId);
    Task<int> CountAsync(int quizId);
    Task ClearAsync(int quizId);
}

public sealed class ParticipantTracker(IConnectionMultiplexer redis, ILogger<ParticipantTracker> log) : IParticipantTracker
{
    private const int StaleSeconds = 120;

    private static string Key(int quizId) => $"qp:quiz:{quizId}:participants";

    public async Task JoinAsync(int quizId, string connectionId)
    {
        try
        {
            var db = redis.GetDatabase();
            await db.SortedSetAddAsync(Key(quizId), connectionId, Now());
            await db.KeyExpireAsync(Key(quizId), TimeSpan.FromDays(1), CommandFlags.FireAndForget);
        }
        catch (RedisException ex)
        {
            log.LogWarning(ex, "Participant join tracking failed");
        }
    }

    public async Task LeaveAsync(int quizId, string connectionId)
    {
        try
        {
            await redis.GetDatabase().SortedSetRemoveAsync(Key(quizId), connectionId);
        }
        catch (RedisException ex)
        {
            log.LogWarning(ex, "Participant leave tracking failed");
        }
    }

    public async Task<int> CountAsync(int quizId)
    {
        try
        {
            var db = redis.GetDatabase();
            var cutoff = Now() - StaleSeconds;
            await db.SortedSetRemoveRangeByScoreAsync(Key(quizId), double.NegativeInfinity, cutoff, flags: CommandFlags.FireAndForget);
            return (int)await db.SortedSetLengthAsync(Key(quizId), cutoff, double.PositiveInfinity);
        }
        catch (RedisException ex)
        {
            log.LogWarning(ex, "Participant count failed");
            return 0;
        }
    }

    public async Task ClearAsync(int quizId)
    {
        try
        {
            await redis.GetDatabase().KeyDeleteAsync(Key(quizId));
        }
        catch (RedisException ex)
        {
            log.LogWarning(ex, "Participant clear failed");
        }
    }

    private static double Now() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
}
