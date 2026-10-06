namespace PanelForge.Domain.Enums;

/// <summary>Vòng đời ChangeRequest (CF3 mục 6.5): Open → Resolved → Verified; Resolved → Open; Open → Rejected.</summary>
public enum ChangeRequestStatus
{
    Open = 1,
    Resolved = 2,
    Verified = 3,
    Rejected = 4
}
