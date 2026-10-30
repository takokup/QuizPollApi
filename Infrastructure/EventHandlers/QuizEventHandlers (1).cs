using Microsoft.AspNetCore.SignalR;
using QuizPollApi.Infrastructure.SignalR;

namespace QuizPollApi.Infrastructure.EventHandlers;

public sealed class CacheInvalidationHandler(ICacheService cache, IQuizCatalog catalog)
    : IEventHandler<ResponseSubmitted>, IEventHandler<ResponseUpdated>, IEventHandler<QuizPublished>,
      IEventHandler<QuizUpdated>, IEventHandler<QuizClosed>
{
    public Task Handle(ResponseSubmitted e, CancellationToken ct) => cache.RemoveAsync(CacheKeys.QuizResults(e.QuizId), ct);

    public Task Handle(ResponseUpdated e, CancellationToken ct) => cache.RemoveAsync(CacheKeys.QuizResults(e.QuizId), ct);

    public Task Handle(QuizPublished e, CancellationToken ct) => catalog.InvalidateAsync(e.QuizId, ct);

    public Task Handle(QuizUpdated e, CancellationToken ct) => Task.WhenAll(
        catalog.InvalidateAsync(e.QuizId, ct),
        cache.RemoveAsync(CacheKeys.QuizResults(e.QuizId), ct));

    public Task Handle(QuizClosed e, CancellationToken ct) => Task.WhenAll(
        catalog.InvalidateAsync(e.QuizId, ct),
        cache.RemoveAsync(CacheKeys.QuizResults(e.QuizId), ct),
        cache.RemoveAsync(CacheKeys.CurrentQuestion(e.QuizId), ct));
}

public sealed class RealtimeEventHandler(
    LiveResultsQueue liveQueue,
    IHubContext<QuizHub, IQuizClient> hub,
    IQuizCatalog catalog,
    IAnalyticsService analytics,
    IParticipantTracker participants)
    : IEventHandler<ResponseSubmitted>, IEventHandler<ResponseUpdated>, IEventHandler<QuizClosed>
{
    public Task Handle(ResponseSubmitted e, CancellationToken ct)
    {
        liveQueue.Mark(e.QuizId);
        return Task.CompletedTask;
    }

    public Task Handle(ResponseUpdated e, CancellationToken ct)
    {
        liveQueue.Mark(e.QuizId);
        return Task.CompletedTask;
    }

    public async Task Handle(QuizClosed e, CancellationToken ct)
    {
        var quiz = await catalog.GetAsync(e.QuizId, ct);
        if (quiz is null) return;

        var results = (await analytics.GetResultsAsync(e.QuizId, ct)).WithCorrectness(quiz, reveal: true);
        var closedAt = quiz.ClosedAt ?? DateTime.UtcNow;

        var publicResults = quiz.ResultVisibility is ResultVisibility.Public or ResultVisibility.AfterClose ? results : null;

        await hub.Clients.Group(HubGroups.Quiz(e.QuizId))
            .QuizClosed(new QuizClosedNotification(e.QuizId, closedAt, results.TotalResponses, publicResults));

        if (publicResults is null)
            await hub.Clients.Group(HubGroups.Owner(e.QuizId))
                .QuizClosed(new QuizClosedNotification(e.QuizId, closedAt, results.TotalResponses, results));

        await participants.ClearAsync(e.QuizId);
    }
}

public sealed class AnalyticsEventHandler(AnalyticsQueue queue)
    : IEventHandler<ResponseSubmitted>, IEventHandler<ResponseUpdated>, IEventHandler<QuizClosed>
{
    public Task Handle(ResponseSubmitted e, CancellationToken ct) => Mark(e.QuizId);
    public Task Handle(ResponseUpdated e, CancellationToken ct) => Mark(e.QuizId);
    public Task Handle(QuizClosed e, CancellationToken ct) => Mark(e.QuizId);

    private Task Mark(int quizId)
    {
        queue.Mark(quizId);
        return Task.CompletedTask;
    }
}
