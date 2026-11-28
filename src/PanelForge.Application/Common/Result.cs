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

    /// <summary>Optional structured business-error payload for endpoints that need more than a message.</summary>
    public object? ErrorDetails { get; }

    private Result(T value)       { IsSuccess = true;  Value = value; }
    private Result(string error, string? errorCode, object? errorDetails)
    {
        IsSuccess = false;
        ErrorMessage = error;
        ErrorCode = errorCode;
        ErrorDetails = errorDetails;
    }

    public static Result<T> Success(T value)       => new(value);
    public static Result<T> Failure(string error, string? errorCode = null, object? errorDetails = null)  => new(error, errorCode, errorDetails);
}

public static class ResultErrorCodes
{
    public const string NotFound = "NOT_FOUND";
    /// <summary>D9b: ấn bản ngôn ngữ chưa phát hành → HTTP 404, không fallback về ngôn ngữ gốc.</summary>
    public const string EditionNotPublished = "EDITION_NOT_PUBLISHED";
    public const string Conflict = "CONFLICT";
    /// <summary>AUD-BE-10: giá ấn bản thay đổi so với giá kỳ vọng client gửi lên → HTTP 409.</summary>
    public const string PriceChanged = "PRICE_CHANGED";
    /// <summary>D11: tài khoản đã có quyền tạo Studio, không mua lại gói mở Studio → HTTP 409.</summary>
    public const string AlreadyOwned = "ALREADY_OWNED";
    public const string HashMismatch = "HASH_MISMATCH";
    public const string BibleGateBlocked = "BIBLE_GATE_BLOCKED";
    public const string ActiveArtworkOnRemovedPage = "ACTIVE_ARTWORK_ON_REMOVED_PAGE";
    public const string CannotDeleteActiveProductionPage = "CANNOT_DELETE_ACTIVE_PRODUCTION_PAGE";
    /// <summary>BR-12: chương Approved/Published, nội dung chỉ đọc → HTTP 409. Mã riêng để FE không nhầm với xung đột phiên bản.</summary>
    public const string ChapterReadOnly = "CHAPTER_READ_ONLY";
    /// <summary>Sai thông tin đăng nhập / OTP / token → HTTP 401.</summary>
    public const string Unauthorized = "UNAUTHORIZED";
    /// <summary>Đã xác thực nhưng không đủ quyền → HTTP 403.</summary>
    public const string Forbidden = "FORBIDDEN";
    /// <summary>Dữ liệu/đề xuất không qua kiểm tra nội dung → HTTP 400.</summary>
    public const string Validation = "VALIDATION_FAILED";
    /// <summary>AI service tắt/không phản hồi → HTTP 503. Chức năng Backend khác vẫn chạy.</summary>
    public const string AiUnavailable = "AI_UNAVAILABLE";
    /// <summary>Không xác thực được với cổng thanh toán (Google Play chưa cấu hình / không phản hồi) → HTTP 503, client thử lại sau.</summary>
    public const string PaymentProviderUnavailable = "PAYMENT_PROVIDER_UNAVAILABLE";
    public const string AiInvalidOutput = "AI_INVALID_OUTPUT";
    public const string AiDisabled = "AI_DISABLED";
    public const string AiQuotaExceeded = "AI_QUOTA_EXCEEDED";
    /// <summary>File rỗng hoặc vượt giới hạn kích thước Admin đặt (<c>upload_limit_policies</c>) → HTTP 413.</summary>
    public const string FileTooLarge = "FILE_TOO_LARGE";
    /// <summary>D12: kho tài nguyên của Producer không đủ để chia cho Studio (tạo mới / tăng hạn mức) → HTTP 409.</summary>
    public const string InsufficientStudioResources = "INSUFFICIENT_STUDIO_RESOURCES";
}
