namespace QuizPollApi.Common;

public sealed record ResponseSubmitted(int QuizId, int ResponseId) : IEvent;

public sealed record ResponseUpdated(int QuizId, int ResponseId) : IEvent;

public sealed record QuizPublished(int QuizId) : IEvent;

public sealed record QuizUpdated(int QuizId) : IEvent;

public sealed record QuizClosed(int QuizId) : IEvent;
