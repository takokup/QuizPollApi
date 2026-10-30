namespace QuizPollApi.Common;

public static class Guard
{
    public static void That(bool condition, string message)
    {
        if (!condition) throw new ValidationException(message);
    }

    public static T Found<T>(T? value, string message) where T : class =>
        value ?? throw new NotFoundException(message);
}
