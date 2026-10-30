namespace QuizPollApi.Features.Questions;

public static class QuestionRules
{
    public const int MaxQuestionsPerQuiz = 100;

    public static void Validate(QuestionInput q, QuizType quizType)
    {
        Guard.That(q is not null, "Question is required.");

        var text = q.Text?.Trim() ?? "";
        Guard.That(text.Length is >= 1 and <= 500, "Question text must be 1-500 characters.");
        Guard.That(q.TimeLimitSeconds is null or (>= 5 and <= 3600), "TimeLimitSeconds must be between 5 and 3600.");

        if (q.ImageUrl is not null)
        {
            Guard.That(
                q.ImageUrl.Length <= 2048 &&
                Uri.TryCreate(q.ImageUrl, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps,
                "ImageUrl must be an absolute https URL.");
        }

        var options = q.Options ?? [];
        switch (q.Type)
        {
            case QuestionType.Single:
            case QuestionType.Multiple:
                Guard.That(options.Count is >= 2 and <= 20, "Choice questions need 2-20 options.");
                Guard.That(options.All(o => o.Text?.Trim().Length is >= 1 and <= 300), "Option text must be 1-300 characters.");
                Guard.That(
                    options.Select(o => o.Text.Trim().ToLowerInvariant()).Distinct().Count() == options.Count,
                    "Option texts must be unique within a question.");
                var correct = options.Count(o => o.IsCorrect);
                if (quizType != QuizType.Quiz)
                    Guard.That(correct == 0, "Only quizzes (type 'quiz') can mark correct answers.");
                else if (q.Type == QuestionType.Single)
                    Guard.That(correct <= 1, "A single-choice question can have at most one correct option.");
                Guard.That(q.MaxRating is null, "MaxRating only applies to rating questions.");
                break;

            case QuestionType.Rating:
                Guard.That(options.Count == 0, "Rating questions cannot have options.");
                Guard.That(q.MaxRating is null or 5 or 10, "MaxRating must be 5 or 10.");
                break;

            case QuestionType.Text:
                Guard.That(options.Count == 0, "Text questions cannot have options.");
                Guard.That(q.MaxRating is null, "MaxRating only applies to rating questions.");
                break;

            default:
                throw new ValidationException("Unknown question type.");
        }
    }

    public static Question Build(QuestionInput q, int orderIndex)
    {
        var question = new Question();
        Apply(question, q, orderIndex);
        return question;
    }

    public static void Apply(Question target, QuestionInput q, int orderIndex)
    {
        target.QuestionText = q.Text.Trim();
        target.QuestionType = q.Type;
        target.OrderIndex = orderIndex;
        target.IsRequired = q.IsRequired;
        target.TimeLimitSeconds = q.TimeLimitSeconds;
        target.ImageUrl = q.ImageUrl;
        target.MaxRating = q.Type == QuestionType.Rating ? q.MaxRating ?? 5 : 5;
        target.Options = (q.Type is QuestionType.Single or QuestionType.Multiple
                ? q.Options!.Select((o, i) => new AnswerOption { OptionText = o.Text.Trim(), OrderIndex = i, IsCorrect = o.IsCorrect })
                : [])
            .ToList();
    }

    public static void ValidateForPublish(Quiz quiz)
    {
        Guard.That(quiz.Questions.Count > 0, "Add at least one question before publishing.");
        if (quiz.Type != QuizType.Quiz) return;

        foreach (var q in quiz.Questions.Where(q => q.QuestionType is QuestionType.Single or QuestionType.Multiple))
        {
            var correct = q.Options.Count(o => o.IsCorrect);
            Guard.That(
                q.QuestionType == QuestionType.Single ? correct == 1 : correct >= 1,
                $"Question '{q.QuestionText}' needs {(q.QuestionType == QuestionType.Single ? "exactly one" : "at least one")} correct option.");
        }
    }
}
