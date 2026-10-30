using Microsoft.AspNetCore.Mvc;
using QuizPollApi.Features.Auth;

namespace QuizPollApi.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(IDispatcher dispatcher) : ControllerBase
{
    [HttpPost("register")]
    public async Task<ActionResult<AuthResultDto>> Register([FromBody] Register.Command command, CancellationToken ct) =>
        Ok(await dispatcher.Send(command, ct));

    [HttpPost("login")]
    public async Task<ActionResult<AuthResultDto>> Login([FromBody] Login.Command command, CancellationToken ct) =>
        Ok(await dispatcher.Send(command, ct));
}
