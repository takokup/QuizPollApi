namespace QuizPollApi.Common;

public abstract class AppException(string message, int statusCode) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

public sealed class ValidationException(string message) : AppException(message, StatusCodes.Status400BadRequest);

public sealed class UnauthorizedException(string message) : AppException(message, StatusCodes.Status401Unauthorized);

public sealed class ForbiddenException(string message) : AppException(message, StatusCodes.Status403Forbidden);

public sealed class NotFoundException(string message) : AppException(message, StatusCodes.Status404NotFound);

public sealed class ConflictException(string message) : AppException(message, StatusCodes.Status409Conflict);

public sealed class RateLimitExceededException(string message, int retryAfterSeconds)
    : AppException(message, StatusCodes.Status429TooManyRequests)
{
    public int RetryAfterSeconds { get; } = retryAfterSeconds;
}
