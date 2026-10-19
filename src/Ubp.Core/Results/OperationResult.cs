namespace Ubp.Core.Results;

public sealed class OperationResult
{
    public bool Success { get; init; }
    public string MessageKey { get; init; } = "";
    public object? Data { get; init; }

    public static OperationResult Ok(string messageKey = "common.saved", object? data = null) =>
        new() { Success = true, MessageKey = messageKey, Data = data };

    public static OperationResult Fail(string messageKey, object? data = null) =>
        new() { Success = false, MessageKey = messageKey, Data = data };
}

public class AppException : Exception
{
    public AppException(string code, int statusCode = 400, IReadOnlyDictionary<string, string>? args = null)
        : base(code)
    {
        Code = code;
        StatusCode = statusCode;
        Args = args ?? new Dictionary<string, string>();
    }

    public string Code { get; }
    public int StatusCode { get; }
    public IReadOnlyDictionary<string, string> Args { get; }
}

public sealed class ForbiddenException : AppException
{
    public ForbiddenException(string code = "error.forbidden")
        : base(code, StatusCodes.Status403Forbidden) { }
}

public sealed class NotFoundAppException : AppException
{
    public NotFoundAppException(string code = "error.not_found")
        : base(code, StatusCodes.Status404NotFound) { }
}

public sealed class ConflictException : AppException
{
    public ConflictException(string code = "error.version_conflict")
        : base(code, StatusCodes.Status409Conflict) { }
}

public sealed class ValidationAppException : AppException
{
    public ValidationAppException(string code, IReadOnlyDictionary<string, string>? args = null)
        : base(code, StatusCodes.Status400BadRequest, args) { }
}

public static class StatusCodes
{
    public const int Status400BadRequest = 400;
    public const int Status403Forbidden = 403;
    public const int Status404NotFound = 404;
    public const int Status409Conflict = 409;
    public const int Status500InternalServerError = 500;
}
