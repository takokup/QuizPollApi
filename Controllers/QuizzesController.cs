using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuizPollApi.Features.Analytics;
using QuizPollApi.Features.Quizzes;
using QuizPollApi.Features.Responses;

namespace QuizPollApi.Controllers;

[ApiController]
[Route("api/quizzes")]
public sealed class QuizzesController(IDispatcher dispatcher) : ControllerBase
{
    private string? AccessCode => Request.Headers["X-Access-Code"].FirstOrDefault();
    private string? SessionToken => Request.Headers["X-Session-Token"].FirstOrDefault();
    private string? CaptchaToken => Request.Headers["X-Captcha-Token"].FirstOrDefault();

    [HttpGet]
    public async Task<ActionResult<PagedResult<QuizSummaryDto>>> List(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] QuizType? type = null,
        [FromQuery] bool mine = false, [FromQuery] string? search = null, CancellationToken ct = default) =>
        Ok(await dispatcher.Query(new GetQuizzes.Query(page, pageSize, type, mine, search), ct));

    [HttpPost, Authorize]
    public async Task<ActionResult<QuizCreatedDto>> Create([FromBody] CreateQuiz.Command command, CancellationToken ct)
    {
        var created = await dispatcher.Send(command, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<QuizDetailDto>> Get(int id, CancellationToken ct) =>
        Ok(await dispatcher.Query(new GetQuizById.Query(id, AccessCode), ct));

    [HttpPost("{id:int}/publish"), Authorize]
    public async Task<IActionResult> Publish(int id, CancellationToken ct)
    {
        await dispatcher.Send(new PublishQuiz.Command(id), ct);
        return NoContent();
    }

    [HttpPost("{id:int}/close"), Authorize]
    public async Task<IActionResult> Close(int id, CancellationToken ct)
    {
        await dispatcher.Send(new CloseQuiz.Command(id), ct);
        return NoContent();
    }

    [HttpGet("{id:int}/questions")]
    public async Task<ActionResult<List<QuestionDto>>> GetQuestions(int id, CancellationToken ct) =>
        Ok(await dispatcher.Query(new GetQuestions.Query(id, AccessCode), ct));

    [HttpPost("{id:int}/questions"), Authorize]
    public async Task<IActionResult> AddQuestion(int id, [FromBody] QuestionInput question, CancellationToken ct)
    {
        var questionId = await dispatcher.Send(new AddQuestion.Command(id, question), ct);
        return Created($"api/quizzes/{id}/questions", new { id = questionId });
    }

    [HttpPut("{id:int}/questions/{questionId:int}"), Authorize]
    public async Task<IActionResult> UpdateQuestion(int id, int questionId, [FromBody] QuestionInput question, CancellationToken ct)
    {
        await dispatcher.Send(new UpdateQuestion.Command(id, questionId, question), ct);
        return NoContent();
    }

    [HttpDelete("{id:int}/questions/{questionId:int}"), Authorize]
    public async Task<IActionResult> DeleteQuestion(int id, int questionId, CancellationToken ct)
    {
        await dispatcher.Send(new DeleteQuestion.Command(id, questionId), ct);
        return NoContent();
    }

    [HttpPost("{id:int}/start")]
    public async Task<ActionResult<StartResponseResult>> Start(int id, CancellationToken ct) =>
        Ok(await dispatcher.Send(new StartResponse.Command(id, AccessCode, CaptchaToken), ct));

    [HttpPost("{id:int}/responses")]
    public async Task<ActionResult<ResponseSubmittedDto>> Submit(int id, [FromBody] List<AnswerDto> answers, CancellationToken ct) =>
        Ok(await dispatcher.Send(new SubmitResponse.Command(id, answers, SessionToken, AccessCode), ct));

    [HttpPut("{id:int}/responses/mine")]
    public async Task<ActionResult<ResponseSubmittedDto>> UpdateMine(int id, [FromBody] List<AnswerDto> answers, CancellationToken ct) =>
        Ok(await dispatcher.Send(new UpdateResponse.Command(id, answers, SessionToken, AccessCode), ct));

    [HttpGet("{id:int}/responses/mine")]
    public async Task<ActionResult<ResponseDto>> GetMine(int id, CancellationToken ct) =>
        Ok(await dispatcher.Query(new GetMyResponse.Query(id, SessionToken), ct));

    [HttpGet("{id:int}/responses"), Authorize]
    public async Task<ActionResult<PagedResult<ResponseDto>>> GetResponses(
        int id, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, [FromQuery] bool includeAnswers = false, CancellationToken ct = default) =>
        Ok(await dispatcher.Query(new GetResponses.Query(id, page, pageSize, includeAnswers), ct));

    [HttpGet("{id:int}/results")]
    public async Task<ActionResult<QuizResultsDto>> Results(int id, CancellationToken ct) =>
        Ok(await dispatcher.Query(new GetQuizResults.Query(id, AccessCode, SessionToken), ct));

    [HttpGet("{id:int}/analytics"), Authorize]
    public async Task<ActionResult<QuizAnalyticsDto>> Analytics(
        int id, [FromQuery] string granularity = "hour", [FromQuery] DateTime? from = null, [FromQuery] DateTime? to = null,
        CancellationToken ct = default) =>
        Ok(await dispatcher.Query(new GetQuizAnalytics.Query(id, granularity, from, to), ct));

    [HttpGet("{id:int}/questions/stats"), Authorize]
    public async Task<ActionResult<List<QuestionStatsDto>>> QuestionStats(int id, [FromQuery] int? questionId = null, CancellationToken ct = default) =>
        Ok(await dispatcher.Query(new GetQuestionStats.Query(id, questionId), ct));

    [HttpGet("{id:int}/export"), Authorize]
    public async Task<IActionResult> Export(int id, [FromQuery] string format = "csv", CancellationToken ct = default)
    {
        var file = await dispatcher.Query(new ExportResults.Query(id, format), ct);
        if (file.Truncated) Response.Headers["X-Export-Truncated"] = "true";
        return File(file.Content, file.ContentType, file.FileName);
    }
}
