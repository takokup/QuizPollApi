namespace QuizPollApi.Infrastructure.Services;

public interface IQuizAccess
{
    Task<QuizDefinition> GetViewableAsync(int quizId, string? accessCode, CancellationToken ct);

    bool IsCreator(QuizDefinition quiz);

    Task EnsureCanViewResultsAsync(QuizDefinition quiz, string? sessionToken, CancellationToken ct);
}

public sealed class QuizAccess(
    IQuizCatalog catalog,
    AppDbContext db,
    ICurrentUser user,
    ISessionTokenService sessions,
    IRateLimitService rateLimiter) : IQuizAccess
{
    public bool IsCreator(QuizDefinition quiz) => user.UserId is { } id && id == quiz.CreatorId;

    public async Task<QuizDefinition> GetViewableAsync(int quizId, string? accessCode, CancellationToken ct)
    {
        var quiz = await catalog.GetAsync(quizId, ct) ?? throw new NotFoundException("Quiz not found.");
        if (IsCreator(quiz)) return quiz;

        if (quiz.IsDraft) throw new NotFoundException("Quiz not found."); // do not reveal unpublished quizzes

        if (quiz.Visibility == QuizVisibility.Private)
        {
            await rateLimiter.CheckAsync("access-code", user.IpHash, 30, ct: ct);
            if (!Helpers.CodesMatch(quiz.AccessCode, accessCode))
                throw new ForbiddenException("A valid access code is required for this quiz.");
        }

        return quiz;
    }

    public async Task EnsureCanViewResultsAsync(QuizDefinition quiz, string? sessionToken, CancellationToken ct)
    {
        if (IsCreator(quiz)) return;

        var allowed = quiz.ResultVisibility switch
        {
            ResultVisibility.Public => true,
            ResultVisibility.AfterClose => quiz.IsClosed(DateTime.UtcNow),
            ResultVisibility.CreatorOnly => false,
            ResultVisibility.AfterVote => await HasCompletedAsync(quiz.Id, sessionToken, ct),
            _ => false
        };

        if (!allowed)
            throw new ForbiddenException(quiz.ResultVisibility switch
            {
                ResultVisibility.AfterVote => "Results are visible after you have voted.",
                ResultVisibility.AfterClose => "Results will be visible once the quiz is closed.",
                _ => "Results are only visible to the quiz creator."
            });
    }

    private async Task<bool> HasCompletedAsync(int quizId, string? sessionToken, CancellationToken ct)
    {
        if (user.UserId is { } uid)
            return await db.Responses.AnyAsync(r => r.QuizId == quizId && r.UserId == uid && r.CompletedAt != null, ct);

        var sessionId = sessions.Validate(sessionToken, quizId);
        if (sessionId is not null)
            return await db.Responses.AnyAsync(r => r.QuizId == quizId && r.SessionId == sessionId && r.CompletedAt != null, ct);

        var ipHash = user.IpHash;
        return await db.Responses.AnyAsync(
            r => r.QuizId == quizId && r.UserId == null && r.IpAddressHash == ipHash && r.CompletedAt != null, ct);
    }
}
