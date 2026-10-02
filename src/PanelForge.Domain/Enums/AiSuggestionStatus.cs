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
    Lettering = 4
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
