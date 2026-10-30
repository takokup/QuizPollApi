namespace QuizPollApi.Features.Shared;

public static class Mapping
{
    public static QuestionDto ToDto(this QuestionDef q, bool revealCorrect) => new(
        q.Id, q.Text, q.Type, q.OrderIndex, q.IsRequired, q.TimeLimitSeconds, q.ImageUrl,
        q.Type == QuestionType.Rating ? q.MaxRating : null,
        q.Options.Select(o => new OptionDto(o.Id, o.Text, o.OrderIndex, revealCorrect ? o.IsCorrect : null)).ToList());

    public static QuizDetailDto ToDetail(this QuizDefinition quiz, bool isCreator, DateTime now)
    {
        var reveal = isCreator || quiz.IsClosed(now);
        return new QuizDetailDto(
            quiz.Id, quiz.CreatorId, quiz.Title, quiz.Description, quiz.Type, quiz.Visibility,
            isCreator ? quiz.AccessCode : null, quiz.IsAnonymousAllowed, quiz.Status(now),
            quiz.StartsAt, quiz.EndsAt, quiz.ResultVisibility,
            quiz.Questions.Select(q => q.ToDto(reveal)).ToList());
    }

    public static QuizResultsDto WithCorrectness(this QuizResultsDto results, QuizDefinition quiz, bool reveal)
    {
        if (!reveal || quiz.Type != QuizType.Quiz) return results;

        var keys = quiz.Questions.ToDictionary(q => q.Id, q => q.Options.ToDictionary(o => o.Id, o => o.IsCorrect));
        return results with
        {
            Questions = results.Questions.Select(q => keys.TryGetValue(q.QuestionId, out var map)
                ? q with { Options = q.Options.Select(o => o with { IsCorrect = map.GetValueOrDefault(o.OptionId) }).ToList() }
                : q).ToList()
        };
    }
}
