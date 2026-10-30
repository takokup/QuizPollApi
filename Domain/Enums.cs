namespace QuizPollApi.Domain;

public enum QuizType { Quiz, Poll, Survey }

public enum QuizVisibility { Public, Private }

public enum QuestionType { Single, Multiple, Rating, Text }

public enum ResultVisibility
{
    Public,
    AfterVote,
    AfterClose,
    CreatorOnly
}

public enum QuizStatus { Draft, Active, Closed }
