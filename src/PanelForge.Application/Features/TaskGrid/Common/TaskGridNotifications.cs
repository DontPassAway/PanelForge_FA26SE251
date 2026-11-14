using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.TaskGrid.Common;

/// <summary>
/// Thông báo của lưới V3 (BR-14). Dùng lại <see cref="NotificationType"/> của Assignment; <c>assignmentId</c> để null,
/// FE mở lưới theo <c>chapterId</c>. Handler thêm vào DbContext cùng lần Save với thay đổi ô.
/// </summary>
internal static class TaskGridNotifications
{
    private static string Cells(int count) => count == 1 ? "1 ô" : $"{count} ô";

    public static Notification Assigned(StageTaskLot lot, int taskCount, string stageName)
    {
        var due = lot.DueDate is { } d ? $" (hạn {d:yyyy-MM-dd HH:mm} UTC)" : "";
        return Notification.Create(lot.AssigneeUserId, NotificationType.AssignmentAssigned,
            $"Bạn được giao {Cells(taskCount)} – {stageName}", (lot.Brief is { Length: > 0 } b ? b : stageName) + due,
            chapterId: lot.ChapterId);
    }

    public static Notification Submitted(Guid reviewerId, Guid chapterId, decimal chapterNumber, int taskCount) => Notification.Create(
        reviewerId, NotificationType.AssignmentSubmitted, $"Có {Cells(taskCount)} mới nộp chờ duyệt",
        $"Chương {chapterNumber}: mở lưới giao việc để duyệt.", chapterId: chapterId);

    public static Notification Unassigned(Guid userId, Guid chapterId, int taskCount) => Notification.Create(
        userId, NotificationType.AssignmentUnassigned, "Bạn đã được gỡ khỏi việc", $"Producer gỡ bạn khỏi {Cells(taskCount)}.",
        chapterId: chapterId);

    public static Notification Approved(Guid userId, Guid chapterId, string stageName) => Notification.Create(
        userId, NotificationType.AssignmentApproved, "Ô việc của bạn đã được duyệt", stageName, chapterId: chapterId);

    public static Notification ChangesRequested(Guid userId, Guid chapterId, string stageName, string comment) => Notification.Create(
        userId, NotificationType.AssignmentChangesRequested, $"Ô {stageName} bị trả về", comment, chapterId: chapterId);

    public static Notification InputOutdated(Guid userId, Guid chapterId, int taskCount, string reason) => Notification.Create(
        userId, NotificationType.AssignmentUpdated, $"Đầu vào của {Cells(taskCount)} đã thay đổi", reason, chapterId: chapterId);

    public static Notification Ready(Guid userId, Guid chapterId, int taskCount) => Notification.Create(
        userId, NotificationType.AssignmentReady, $"Đến lượt bạn: {Cells(taskCount)} đã sẵn sàng", "Công đoạn trước đã được duyệt.",
        chapterId: chapterId);

    /// <summary>
    /// Sau khi <paramref name="approved"/> được duyệt: ô hạ nguồn trực tiếp đang Assigned và giờ đủ tiền đề → báo người nhận
    /// (một thông báo cho mỗi người). Truyền <paramref name="chapterTasks"/> đã phản ánh trạng thái mới.
    /// </summary>
    public static async Task AddReadyAsync(IPanelForgeDbContext db, IReadOnlyList<StageTask> approved,
        IReadOnlyList<PipelineStage> stages, IReadOnlyList<StageTask> chapterTasks, Guid chapterId, CancellationToken ct)
    {
        var approvedStageIds = approved.Select(t => t.PipelineStageId).ToHashSet();
        var ready = chapterTasks.Where(t => t.Status == StageTaskStatus.Assigned && t.LotId is not null
                && stages.FirstOrDefault(s => s.Id == t.PipelineStageId) is { } stage
                && StageDependencies.DirectPrerequisites(stage, stages).Any(p => approvedStageIds.Contains(p.Id))
                && approved.Any(a => a.PageId is null || t.PageId is null || a.PageId == t.PageId)
                && TaskGridRules.PrerequisitesApproved(t, stages, chapterTasks))
            .ToList();
        if (ready.Count == 0) return;

        var assigneeByLot = await AssigneesByLotAsync(db, ready, ct);
        foreach (var group in ready.GroupBy(t => assigneeByLot.GetValueOrDefault(t.LotId!.Value)).Where(g => g.Key != Guid.Empty))
            db.Notifications.Add(Ready(group.Key, chapterId, group.Count()));
    }

    /// <summary>Người nhận của từng lô, cho các ô đang có lô.</summary>
    public static async Task<Dictionary<Guid, Guid>> AssigneesByLotAsync(IPanelForgeDbContext db, IEnumerable<StageTask> tasks, CancellationToken ct)
    {
        var lotIds = tasks.Select(t => t.LotId).OfType<Guid>().Distinct().ToList();
        return lotIds.Count == 0
            ? []
            : await db.StageTaskLots.AsNoTracking().Where(l => lotIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, l => l.AssigneeUserId, ct);
    }
}
