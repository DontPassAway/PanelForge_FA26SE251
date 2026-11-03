using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;

namespace PanelForge.Domain.Services;

/// <summary>
/// Domain State Machine Engine for Comic/Webtoon Chapter Production.
/// Enforces: Planning -> Scripting -> InReview -> Approved -> Storyboarding -> Drawing -> Published.
/// When in Scripting, only assigned Writer (or Producer) can edit.
/// When Producer approves (InReview -> Approved), the script is LOCKED (IsLocked = true)
/// to freeze the narrative baseline for the Artist's storyboard phase.
/// </summary>
public static class ChapterProductionStateMachine
{
    private static readonly Dictionary<ProductionChapterStatus, HashSet<ProductionChapterStatus>> AllowedTransitions = new()
    {
        [ProductionChapterStatus.Planning] = [ProductionChapterStatus.Scripting],
        [ProductionChapterStatus.Scripting] = [ProductionChapterStatus.InReview],
        [ProductionChapterStatus.InReview] = [ProductionChapterStatus.Approved, ProductionChapterStatus.Scripting],
        [ProductionChapterStatus.Approved] = [ProductionChapterStatus.Storyboarding],
        [ProductionChapterStatus.Storyboarding] = [ProductionChapterStatus.Drawing],
        [ProductionChapterStatus.Drawing] = [ProductionChapterStatus.Published],
        [ProductionChapterStatus.Published] = []
    };

    public static bool CanTransition(ProductionChapterStatus currentStatus, ProductionChapterStatus targetStatus)
    {
        if (AllowedTransitions.TryGetValue(currentStatus, out var nextStatuses))
        {
            return nextStatuses.Contains(targetStatus);
        }
        return false;
    }

    /// <summary>
    /// Executes a validated state machine transition on a Chapter and its associated ChapterScript.
    /// Automatically locks the script when moving to Approved.
    /// </summary>
    public static void Transition(
        ChapterScript script,
        ProductionChapterStatus currentStatus,
        ProductionChapterStatus targetStatus,
        Guid actorUserId,
        ProjectRoleInProject actorRole,
        Guid? assignedProducerId = null,
        Guid? assignedWriterId = null)
    {
        ArgumentNullException.ThrowIfNull(script);

        if (!CanTransition(currentStatus, targetStatus))
        {
            throw new InvalidOperationException(
                $"Invalid production state transition from '{currentStatus}' to '{targetStatus}'.");
        }

        // Role authorization gates
        switch (targetStatus)
        {
            case ProductionChapterStatus.Scripting when currentStatus == ProductionChapterStatus.Planning:
                // Producer initiates scripting phase
                if (actorRole != ProjectRoleInProject.Producer)
                    throw new UnauthorizedAccessException("Only a Producer can start the Scripting phase from Planning.");
                break;

            case ProductionChapterStatus.InReview:
                // Writer (or Producer) submits script for review
                if (actorRole != ProjectRoleInProject.Writer && actorRole != ProjectRoleInProject.Producer)
                    throw new UnauthorizedAccessException("Only the assigned Writer or Producer can submit the script for review.");
                if (assignedWriterId.HasValue && actorRole == ProjectRoleInProject.Writer && actorUserId != assignedWriterId.Value)
                    throw new UnauthorizedAccessException("Only the assigned Writer for this chapter can submit it for review.");
                break;

            case ProductionChapterStatus.Approved:
                // Only Producer can approve the script
                if (actorRole != ProjectRoleInProject.Producer)
                    throw new UnauthorizedAccessException("Only a Producer can approve the chapter script.");
                if (assignedProducerId.HasValue && actorUserId != assignedProducerId.Value)
                    throw new UnauthorizedAccessException("Only the assigned Producer for this chapter can approve it.");
                
                // Narrative freeze: strictly lock the script
                script.Lock(actorUserId);
                break;

            case ProductionChapterStatus.Scripting when currentStatus == ProductionChapterStatus.InReview:
                // Producer rejects/requests revision
                if (actorRole != ProjectRoleInProject.Producer)
                    throw new UnauthorizedAccessException("Only a Producer can return a script in review back to Scripting.");
                // Script remains unlocked for Writer edits
                if (script.IsLocked)
                    script.Unlock(actorUserId);
                break;

            case ProductionChapterStatus.Storyboarding:
            case ProductionChapterStatus.Drawing:
            case ProductionChapterStatus.Published:
                if (actorRole != ProjectRoleInProject.Producer)
                    throw new UnauthorizedAccessException($"Only a Producer can advance the chapter to '{targetStatus}'.");
                break;
        }
    }
}
