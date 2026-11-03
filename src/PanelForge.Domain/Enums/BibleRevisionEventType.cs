namespace PanelForge.Domain.Enums;

/// <summary>Một bước trên dòng thời gian duyệt của một phiên bản Bible (không làm tăng VersionNumber).</summary>
public enum BibleRevisionEventType
{
    Created = 1,
    ConceptSubmitted = 2,
    ReworkRequested = 3,
    Endorsed = 4,
    Locked = 5,
    Superseded = 6
}
