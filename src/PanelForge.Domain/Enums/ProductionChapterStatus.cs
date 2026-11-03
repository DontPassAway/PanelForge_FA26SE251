namespace PanelForge.Domain.Enums;

/// <summary>
/// Core Production Architecture Chapter State Machine.
/// Planning -> Scripting -> InReview -> Approved -> Storyboarding -> Drawing -> Published.
/// </summary>
public enum ProductionChapterStatus
{
    Planning = 1,
    Scripting = 2,
    InReview = 3,
    Approved = 4,
    Storyboarding = 5,
    Drawing = 6,
    Published = 7
}
