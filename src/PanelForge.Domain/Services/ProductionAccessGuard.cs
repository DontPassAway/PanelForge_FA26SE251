using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;

namespace PanelForge.Domain.Services;

/// <summary>
/// Domain Security & Access Control Guard for Chapter Script editing.
/// Enforces Zero-Trust RBAC and narrative baseline freezing.
/// </summary>
public static class ProductionAccessGuard
{
    /// <summary>
    /// Validates whether a user can edit the script of a chapter.
    /// Throws InvalidOperationException if locked or wrong status.
    /// Throws UnauthorizedAccessException if user lacks assignment/role.
    /// </summary>
    public static void EnsureCanEditScript(
        ChapterScript script,
        ProductionChapterStatus currentStatus,
        Guid userId,
        Guid producerId,
        Guid? writerId)
    {
        ArgumentNullException.ThrowIfNull(script);

        if (script.IsLocked)
        {
            throw new InvalidOperationException(
                "Script is locked and cannot be edited. It has been approved to freeze the narrative baseline for production.");
        }

        if (currentStatus != ProductionChapterStatus.Scripting)
        {
            throw new InvalidOperationException(
                $"Script editing is only permitted when Chapter status is 'Scripting' (current: '{currentStatus}').");
        }

        bool isAssignedWriter = writerId.HasValue && writerId.Value == userId;
        bool isProducer = producerId == userId;

        if (!isAssignedWriter && !isProducer)
        {
            throw new UnauthorizedAccessException(
                "Only the assigned Writer or the Producer is authorized to edit the chapter script.");
        }
    }

    /// <summary>
    /// Returns boolean result for non-throwing evaluation (e.g. UI reflection).
    /// </summary>
    public static bool CanEditScript(
        ChapterScript script,
        ProductionChapterStatus currentStatus,
        Guid userId,
        Guid producerId,
        Guid? writerId,
        out string? reason)
    {
        try
        {
            EnsureCanEditScript(script, currentStatus, userId, producerId, writerId);
            reason = null;
            return true;
        }
        catch (Exception ex)
        {
            reason = ex.Message;
            return false;
        }
    }
}
