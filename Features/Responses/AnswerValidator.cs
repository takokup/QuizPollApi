namespace QuizPollApi.Features.Responses;

public static class AnswerValidator
{
    public sealed record Result(List<ResponseAnswer> Answers, int? Score, int? MaxScore);

    public static Result Build(QuizDefinition quiz, IReadOnlyList<AnswerDto>? input)
    {
        input ??= [];
        Guard.That(input.Count <= quiz.Questions.Count, "More answers than questions.");
        Guard.That(input.Select(a => a.QuestionId).Distinct().Count() == input.Count, "Each question can only be answered once.");

        var questions = quiz.Questions.ToDictionary(q => q.Id);
        var now = DateTime.UtcNow;
        var answers = new List<ResponseAnswer>();
        var score = 0;
        var maxScore = 0;

        foreach (var a in input)
        {
            Guard.That(questions.TryGetValue(a.QuestionId, out var q), $"Question {a.QuestionId} does not belong to this quiz.");
            q = questions[a.QuestionId];

            var answer = new ResponseAnswer { QuestionId = q.Id, AnsweredAt = now };

            switch (q.Type)
            {
                case QuestionType.Single:
                case QuestionType.Multiple:
                    var selected = (a.SelectedOptionIds ?? []).Distinct().ToArray();
                    Guard.That(a.RatingValue is null && a.TextAnswer is null, $"Question {q.Id}: only selectedOptionIds is allowed.");
                    Guard.That(selected.Length > 0, $"Question {q.Id}: select at least one option.");
                    if (q.Type == QuestionType.Single)
                        Guard.That(selected.Length == 1, $"Question {q.Id}: select exactly one option.");
                    var valid = q.Options.Select(o => o.Id).ToHashSet();
                    Guard.That(selected.All(valid.Contains), $"Question {q.Id}: invalid option.");
                    answer.SelectedOptionIds = selected;
                    break;

                case QuestionType.Rating:
                    Guard.That(a.SelectedOptionIds is null or { Length: 0 } && a.TextAnswer is null, $"Question {q.Id}: only ratingValue is allowed.");
                    Guard.That(a.RatingValue is >= 1 && a.RatingValue <= q.MaxRating, $"Question {q.Id}: rating must be between 1 and {q.MaxRating}.");
                    answer.RatingValue = a.RatingValue;
                    break;

                case QuestionType.Text:
                    Guard.That(a.SelectedOptionIds is null or { Length: 0 } && a.RatingValue is null, $"Question {q.Id}: only textAnswer is allowed.");
                    var text = a.TextAnswer?.Trim() ?? "";
                    Guard.That(text.Length is >= 1 and <= 2000, $"Question {q.Id}: text must be 1-2000 characters.");
                    answer.TextAnswer = text;
                    break;
            }

            answers.Add(answer);
        }

        var answered = answers.Select(a => a.QuestionId).ToHashSet();
        var missing = quiz.Questions.FirstOrDefault(q => q.IsRequired && !answered.Contains(q.Id));
        Guard.That(missing is null, $"Question {missing?.Id} is required.");

        if (quiz.Type != QuizType.Quiz) return new Result(answers, null, null);

        foreach (var q in quiz.Questions.Where(q => q.Type is QuestionType.Single or QuestionType.Multiple))
        {
            maxScore++;
            var given = answers.FirstOrDefault(a => a.QuestionId == q.Id);
            if (given is null) continue;

            var correct = q.Options.Where(o => o.IsCorrect).Select(o => o.Id).ToHashSet();
            if (correct.Count > 0 && correct.SetEquals(given.SelectedOptionIds)) score++;
        }

        return new Result(answers, score, maxScore);
    }
}
