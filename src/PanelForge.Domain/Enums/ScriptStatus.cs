namespace PanelForge.Domain.Enums;

/// <summary>
/// Vòng đời kịch bản của một chương (D12, 01_SCRIPT_PLAN): Draft ↔ InReview → Frozen.
/// Writer nộp (Draft → InReview) và chỉ Writer rút về (InReview → Draft); Editor chỉ góp ý, không đổi trạng thái.
/// Producer bấm Start Production (InReview → Frozen) hoặc mở lại kèm lý do (Frozen → Draft).
/// </summary>
public enum ScriptStatus
{
    Draft = 1,
    InReview = 2,
    Frozen = 3
}
