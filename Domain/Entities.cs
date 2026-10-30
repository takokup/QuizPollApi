namespace QuizPollApi.Domain;

public class User
{
    public int Id { get; set; }
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string DisplayName { get; set; } = "";

    public string? Country { get; set; }
    public string? AgeGroup { get; set; }
    public string? Gender { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class Quiz
{
    public int Id { get; set; }
    public int CreatorId { get; set; }
    public User? Creator { get; set; }

    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public QuizType Type { get; set; }
    public QuizVisibility Visibility { get; set; }
    public string? AccessCode { get; set; }
    public bool IsAnonymousAllowed { get; set; } = true;
    public DateTime? StartsAt { get; set; }
    public DateTime? EndsAt { get; set; }

    public bool IsActive { get; set; }
    public DateTime? ClosedAt { get; set; }
    public ResultVisibility ResultVisibility { get; set; } = ResultVisibility.Public;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public uint Version { get; set; }

    public List<Question> Questions { get; set; } = [];

    public QuizStatus Status => ClosedAt is not null ? QuizStatus.Closed : IsActive ? QuizStatus.Active : QuizStatus.Draft;
}

public class Question
{
    public int Id { get; set; }
    public int QuizId { get; set; }
    public Quiz? Quiz { get; set; }

    public string QuestionText { get; set; } = "";
    public QuestionType QuestionType { get; set; }
    public int OrderIndex { get; set; }
    public bool IsRequired { get; set; } = true;
    public int? TimeLimitSeconds { get; set; }
    public string? ImageUrl { get; set; }

    public int MaxRating { get; set; } = 5;

    public List<AnswerOption> Options { get; set; } = [];
}

public class AnswerOption
{
    public int Id { get; set; }
    public int QuestionId { get; set; }
    public Question? Question { get; set; }

    public string OptionText { get; set; } = "";
    public int OrderIndex { get; set; }
    public bool IsCorrect { get; set; }
}

public class Response
{
    public int Id { get; set; }
    public int QuizId { get; set; }
    public Quiz? Quiz { get; set; }

    public int? UserId { get; set; }
    public User? User { get; set; }
    public string? SessionId { get; set; }

    public string IpAddressHash { get; set; } = "";

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public int? Score { get; set; }

    public uint Version { get; set; }

    public List<ResponseAnswer> Answers { get; set; } = [];
}

public class ResponseAnswer
{
    public int Id { get; set; }
    public int ResponseId { get; set; }
    public Response? Response { get; set; }

    public int QuestionId { get; set; }
    public Question? Question { get; set; }

    public int[] SelectedOptionIds { get; set; } = [];
    public string? TextAnswer { get; set; }
    public int? RatingValue { get; set; }
    public DateTime AnsweredAt { get; set; } = DateTime.UtcNow;
}

public class QuizAnalytics
{
    public int Id { get; set; }
    public int QuizId { get; set; }
    public Quiz? Quiz { get; set; }

    public int TotalResponses { get; set; }
    public double AverageCompletionTime { get; set; }
    public double CompletionRate { get; set; }
    public DateTime LastCalculatedAt { get; set; } = DateTime.UtcNow;
}
