namespace QuizPollApi.Common;

public sealed class GlobalRateLimitMiddleware(RequestDelegate next)
{
    public async Task Invoke(HttpContext context, ICurrentUser user, IRateLimitService limiter, IOptions<RateLimitOptions> options)
    {
        var path = context.Request.Path;
        if (path.StartsWithSegments("/api") || path.StartsWithSegments("/hubs"))
            await limiter.CheckAsync("global", user.IpHash, options.Value.GlobalPerIpPerMinute, ct: context.RequestAborted);

        await next(context);
    }
}
