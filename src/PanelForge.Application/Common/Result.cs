namespace PanelForge.Application.Common;

/// <summary>
/// Generic result wrapper — thay thế việc throw exception cho business errors.
/// </summary>
public sealed class Result<T>
{
    public bool IsSuccess { get; }
    public T? Value { get; }
    public string? ErrorMessage { get; }

    /// <summary>Mã lỗi máy đọc được (xem <see cref="ResultErrorCodes"/>), null nếu là lỗi nghiệp vụ chung.</summary>
    public string? ErrorCode { get; }

    private Result(T value)       { IsSuccess = true;  Value = value; }
    private Result(string error, string? errorCode)  { IsSuccess = false; ErrorMessage = error; ErrorCode = errorCode; }

    public static Result<T> Success(T value)       => new(value);
    public static Result<T> Failure(string error, string? errorCode = null)  => new(error, errorCode);
}

public static class ResultErrorCodes
{
    public const string NotFound = "NOT_FOUND";
    public const string Conflict = "CONFLICT";
}
