using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace QuizPollApi.Infrastructure.SignalR;

public class QuizHub(
    IQuizCatalog catalog,
    IParticipantTracker participants,
    ParticipantsQueue participantsQueue,
    IRateLimitService rateLimiter,
    IIpHasher ipHasher,
    ICacheService cache,
    IOptions<RateLimitOptions> limits,
    ILogger<QuizHub> logger) : Hub<IQuizClient>
{
    private const string JoinedKey = "joined-quizzes";

    private HashSet<int> Joined =>
        (HashSet<int>)(Context.Items[JoinedKey] ??= new HashSet<int>());

    public async Task JoinQuiz(int quizId, string? accessCode)
    {
        await rateLimiter.CheckAsync("hub-join", ClientIpHash(), limits.Value.HubJoinPerIpPerMinute);

        var quiz = await catalog.GetAsync(quizId) ?? throw new HubException("Quiz not found.");
        if (!IsCreator(quiz))
        {
            if (quiz.IsDraft) throw new HubException("Quiz not found.");
            if (quiz.Visibility == QuizVisibility.Private && !Helpers.CodesMatch(quiz.AccessCode, accessCode))
                throw new HubException("A valid access code is required.");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, HubGroups.Quiz(quizId));
        Joined.Add(quizId);
        await participants.JoinAsync(quizId, Context.ConnectionId);
        participantsQueue.Mark(quizId); // the count is broadcast in batches by LiveResultsBroadcaster

        var current = await cache.GetAsync<QuestionChangedNotification>(CacheKeys.CurrentQuestion(quizId));
        if (current is not null) await Clients.Caller.QuestionChanged(current);
        await Clients.Caller.ParticipantUpdate(await participants.CountAsync(quizId));

        logger.LogInformation("Connection {ConnectionId} joined quiz {QuizId}", Context.ConnectionId, quizId);
    }

    public async Task LeaveQuiz(int quizId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, HubGroups.Quiz(quizId));
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, HubGroups.Owner(quizId));
        Joined.Remove(quizId);
        await participants.LeaveAsync(quizId, Context.ConnectionId);
        participantsQueue.Mark(quizId);
    }

    public async Task Heartbeat(int quizId)
    {
        if (Joined.Contains(quizId))
            await participants.JoinAsync(quizId, Context.ConnectionId);
    }

    [Authorize]
    public async Task JoinAsPresenter(int quizId)
    {
        var quiz = await catalog.GetAsync(quizId) ?? throw new HubException("Quiz not found.");
        if (!IsCreator(quiz)) throw new HubException("Only the quiz creator can present.");

        await Groups.AddToGroupAsync(Context.ConnectionId, HubGroups.Quiz(quizId));
        await Groups.AddToGroupAsync(Context.ConnectionId, HubGroups.Owner(quizId));
        Joined.Add(quizId);
        await participants.JoinAsync(quizId, Context.ConnectionId);
        participantsQueue.Mark(quizId);
    }

    [Authorize]
    public async Task NavigateToQuestion(int quizId, int questionId)
    {
        var quiz = await catalog.GetAsync(quizId) ?? throw new HubException("Quiz not found.");
        if (!IsCreator(quiz)) throw new HubException("Only the quiz creator can navigate.");
        if (!quiz.IsOpen(DateTime.UtcNow)) throw new HubException("The quiz is not open.");

        var ordered = quiz.Questions.OrderBy(q => q.OrderIndex).ToList();
        var index = ordered.FindIndex(q => q.Id == questionId);
        if (index < 0) throw new HubException("Question not found.");

        var now = DateTime.UtcNow;
        var limit = ordered[index].TimeLimitSeconds;
        var notification = new QuestionChangedNotification(quizId, questionId, index, now, limit is { } s ? now.AddSeconds(s) : null);

        await cache.SetAsync(CacheKeys.CurrentQuestion(quizId), notification, TimeSpan.FromHours(12));
        await Clients.Group(HubGroups.Quiz(quizId)).QuestionChanged(notification);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        foreach (var quizId in Joined.ToArray())
        {
            await participants.LeaveAsync(quizId, Context.ConnectionId);
            participantsQueue.Mark(quizId);
        }

        await base.OnDisconnectedAsync(exception);
    }

    private bool IsCreator(QuizDefinition quiz) => Context.User.UserId() is { } id && id == quiz.CreatorId;

    private string ClientIpHash() => ipHasher.Hash(Context.GetHttpContext()?.Connection.RemoteIpAddress);
}
