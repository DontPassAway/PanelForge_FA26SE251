namespace PanelForge.Domain.Enums;

/// <summary>Trạng thái một lần nộp (CF3 mục 6.1): Pending → Approved | Rejected; Pending → Superseded khi nộp lại.</summary>
public enum SubmissionStatus
{
    Pending = 1,
    Approved = 2,
    Rejected = 3,
    Superseded = 4
}
