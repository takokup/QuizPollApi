namespace QuizPollApi.Infrastructure.SignalR;
public interface IQuizClient
{
        Task ReceiveUpdate(ResponseUpdate update);

    Task ParticipantUpdate(int count);
    Task QuestionChanged(QuestionChangedNotification notification);
    Task QuizClosed(QuizClosedNotification notification);
}

public static class HubGroups
{
    public static string Quiz(int quizId) => $"quiz-{quizId}";
    public static string Owner(int quizId) => $"quiz-{quizId}-owner";
}
