namespace PanelForge.Domain.Enums;

/// <summary>
/// Trạng thái của một ấn bản ngôn ngữ (ChapterEdition) theo BR-34.
/// Draft -> InReview -> EditorApproved -> Approved -> Published
/// </summary>
public enum EditionStatus
{
    Draft = 0,
    InReview = 1,
    EditorApproved = 2,
    Approved = 3,
    Published = 4
}
