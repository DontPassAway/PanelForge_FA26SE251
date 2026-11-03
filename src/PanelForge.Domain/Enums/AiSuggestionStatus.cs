namespace PanelForge.Domain.Enums;

public enum AiSuggestionStatus
{
    Pending = 1,
    Accepted = 2,
    Rejected = 3,
    ChangesRequested = 4
}

public enum AiSuggestionKind
{
    Script = 1,
    Layout = 2,
    /// <summary>Prompt ảnh cho panel (áp dụng = ghi revision ImagePrompt; không sinh ảnh).</summary>
    ImagePrompt = 3,
    /// <summary>Rút gọn thoại (áp dụng = revision Script mới, dòng gốc còn trong revision cũ).</summary>
    Lettering = 4,
    /// <summary>Copilot: viết lại / rút gọn MỘT dòng thoại (áp dụng = revision Script mới).</summary>
    LineRewrite = 5,
    /// <summary>Copilot Publisher: synopsis + genre của Series (áp dụng = cập nhật Series; logline/teaser/ageRating chỉ tham khảo).</summary>
    SeriesMetadata = 6,
    /// <summary>Ảnh thumbnail AI cho panel (áp dụng = AssetReference phiên bản mới của panel; ảnh cũ giữ nguyên).</summary>
    Thumbnail = 7
}

public enum AiStepStatus
{
    Succeeded = 1,
    Failed = 2
}

/// <summary>Loại nội dung có revision (script / layout của một Scene).</summary>
public enum ContentRevisionKind
{
    Script = 1,
    Layout = 2,
    /// <summary>Prompt ảnh của panel (revision append-only, chưa sinh ảnh).</summary>
    ImagePrompt = 3
}
