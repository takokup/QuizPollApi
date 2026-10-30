using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace QuizPollApi.Common;

public sealed class ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger)
{
    public async Task Invoke(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (context.Response.HasStarted)
            {
                logger.LogError(ex, "Unhandled exception after response started");
                throw;
            }

            var (status, title) = ex switch
            {
                AppException app => (app.StatusCode, app.Message),
                DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, "The resource was modified concurrently. Please retry."),
                _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred.")
            };

            if (status >= 500)
                logger.LogError(ex, "Unhandled exception");

            if (ex is RateLimitExceededException rl)
                context.Response.Headers.RetryAfter = rl.RetryAfterSeconds.ToString();

            context.Response.Clear();
            context.Response.StatusCode = status;
            await context.Response.WriteAsJsonAsync(new ProblemDetails
            {
                Status = status,
                Title = title,
                Instance = context.Request.Path
            });
        }
    }
}
