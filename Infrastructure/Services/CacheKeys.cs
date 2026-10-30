namespace QuizPollApi.Infrastructure.Services;

public static class CacheKeys
{
    public static string QuizDefinition(int quizId) => $"quiz:{quizId}:def";
    public static string QuizResults(int quizId) => $"quiz:{quizId}:results";
    public static string QuizAnalytics(int quizId, string granularity, long fromTicks, long toTicks) =>
        $"quiz:{quizId}:analytics:{granularity}:{fromTicks}:{toTicks}";
    public static string CurrentQuestion(int quizId) => $"quiz:{quizId}:current-question";
}
