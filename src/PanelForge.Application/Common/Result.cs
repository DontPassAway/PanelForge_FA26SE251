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
    /// <summary>Sai thông tin đăng nhập / OTP / token → HTTP 401.</summary>
    public const string Unauthorized = "UNAUTHORIZED";
    /// <summary>Đã xác thực nhưng không đủ quyền → HTTP 403.</summary>
    public const string Forbidden = "FORBIDDEN";
    /// <summary>Dữ liệu/đề xuất không qua kiểm tra nội dung → HTTP 400.</summary>
    public const string Validation = "VALIDATION_FAILED";
    /// <summary>AI service tắt/không phản hồi → HTTP 503. Chức năng Backend khác vẫn chạy.</summary>
    public const string AiUnavailable = "AI_UNAVAILABLE";
    public const string AiDisabled = "AI_DISABLED";
    public const string AiQuotaExceeded = "AI_QUOTA_EXCEEDED";
}
