using Microsoft.AspNetCore.SignalR;
using QuizPollApi.Infrastructure.SignalR;

namespace QuizPollApi.BackgroundServices;
public sealed class LiveResultsBroadcaster(
    LiveResultsQueue resultsQueue,
    ParticipantsQueue participantsQueue,
    IServiceScopeFactory scopes,
    IHubContext<QuizHub, IQuizClient> hub,
    IParticipantTracker participants,
    ILogger<LiveResultsBroadcaster> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(500));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            var resultIds = resultsQueue.Drain();
            var participantIds = participantsQueue.Drain();

            foreach (var quizId in resultIds.Union(participantIds))
            {
                try
                {
                    await Flush(quizId, resultIds.Contains(quizId), stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Live update for quiz {QuizId} failed", quizId);
                    resultsQueue.Mark(quizId); // retry on the next tick
                }
            }
        }
    }

    private async Task Flush(int quizId, bool resultsChanged, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var catalog = scope.ServiceProvider.GetRequiredService<IQuizCatalog>();
        var quiz = await catalog.GetAsync(quizId, ct);
        if (quiz is null) return;

        var count = await participants.CountAsync(quizId);
        await hub.Clients.Group(HubGroups.Quiz(quizId)).ParticipantUpdate(count);

        if (!resultsChanged) return;

        var analytics = scope.ServiceProvider.GetRequiredService<IAnalyticsService>();
        var results = (await analytics.GetResultsAsync(quizId, ct)).WithCorrectness(quiz, quiz.IsClosed(DateTime.UtcNow));

        var isPublic = quiz.ResultVisibility == ResultVisibility.Public;
        await hub.Clients.Group(HubGroups.Quiz(quizId))
            .ReceiveUpdate(new ResponseUpdate(quizId, results.TotalResponses, count, isPublic ? results : null));
        if (!isPublic)
            await hub.Clients.Group(HubGroups.Owner(quizId))
                .ReceiveUpdate(new ResponseUpdate(quizId, results.TotalResponses, count, results));
    }
}

public sealed class AnalyticsRefresher(AnalyticsQueue queue, IServiceScopeFactory scopes, ILogger<AnalyticsRefresher> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            foreach (var quizId in queue.Drain())
            {
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<IAnalyticsService>().RecalculateAsync(quizId, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Analytics refresh for quiz {QuizId} failed", quizId);
                    queue.Mark(quizId);
                }
            }
        }
    }
}

public sealed class QuizLifecycleService(IServiceScopeFactory scopes, ILogger<QuizLifecycleService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var events = scope.ServiceProvider.GetRequiredService<IEventPublisher>();

                var closed = await Sql.QueryAsync(db, """
                    UPDATE quizzes SET is_active = FALSE, closed_at = now()
                    WHERE is_active AND closed_at IS NULL AND ends_at IS NOT NULL AND ends_at <= now()
                    RETURNING id
                    """, r => r.GetInt32(0), stoppingToken);

                foreach (var quizId in closed)
                    await events.PublishAsync(new QuizClosed(quizId), stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Quiz lifecycle check failed");
            }
        }
    }
}
