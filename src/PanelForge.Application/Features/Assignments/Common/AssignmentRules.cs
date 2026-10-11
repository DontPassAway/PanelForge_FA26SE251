using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Assignments.Common;

/// <summary>Luật dùng chung của Create/Update Assignment (BR-03, BR-07, BR-12).</summary>
internal static class AssignmentRules
{
    /// <summary>BR-12: Chapter đã Approved/Locked/Published thì không giao hay đổi việc.</summary>
    public static string? ChapterNotEditableReason(Chapter chapter) =>
        chapter.Status == ChapterStatus.InProduction
            ? null
            : $"Chapter đang ở trạng thái {chapter.Status}, không thể thay đổi việc.";

    /// <summary>Người nhận thông báo "đã nộp": Editor của Workspace; nếu chưa có Editor thì Producer. Loại trừ người nộp.</summary>
    public static async Task<List<Guid>> ReviewerUserIdsAsync(
        IPanelForgeDbContext db, Guid workspaceId, Guid excludeUserId, CancellationToken ct)
    {
        async Task<List<Guid>> WithRole(WorkspaceRole role) => await db.WorkspaceMembers.AsNoTracking()
            .Where(m => m.WorkspaceId == workspaceId && m.Role == role && m.UserId != excludeUserId)
            .Select(m => m.UserId).ToListAsync(ct);

        var editors = await WithRole(WorkspaceRole.Editor);
        return editors.Count > 0 ? editors : await WithRole(WorkspaceRole.Producer);
    }

    /// <summary>BR-07: người nhận phải là thành viên Workspace; vai trò lấy từ membership, phải khớp stage.</summary>
    public static async Task<Result<WorkspaceRole>> ResolveAssigneeRoleAsync(
        IPanelForgeDbContext db, Guid workspaceId, Guid assigneeUserId, PipelineStage stage, CancellationToken ct)
    {
        if (assigneeUserId == Guid.Empty)
            return Result<WorkspaceRole>.Failure("Assignment phải có người nhận.", ResultErrorCodes.Validation);

        var member = await db.WorkspaceMembers.AsNoTracking()
            .FirstOrDefaultAsync(m => m.WorkspaceId == workspaceId && m.UserId == assigneeUserId, ct);
        if (member is null)
            return Result<WorkspaceRole>.Failure("Người nhận không phải thành viên của Workspace.", ResultErrorCodes.Validation);

        if (stage.AllowedRole is { } allowed && allowed != member.Role)
            return Result<WorkspaceRole>.Failure(
                $"Stage '{stage.Name}' dành cho vai trò {allowed}, người nhận là {member.Role}.", ResultErrorCodes.Validation);

        return Result<WorkspaceRole>.Success(member.Role);
    }

    /// <summary>
    /// Kiểm tra đồ thị phụ thuộc đa nhiệm (DAG): mọi dependency phải thuộc cùng Chapter và không tạo chu trình (Cycle).
    /// </summary>
    public static async Task<string?> ValidateDependenciesAsync(
        IPanelForgeDbContext db, Guid chapterId, Guid? selfId, IEnumerable<Guid>? dependsOnIds, CancellationToken ct)
    {
        var depList = dependsOnIds?
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList() ?? [];

        if (depList.Count == 0) return null;

        if (selfId.HasValue && depList.Contains(selfId.Value))
            return "Assignment không thể phụ thuộc vào chính nó.";

        var assignmentsInChapter = await db.Assignments.AsNoTracking()
            .Where(a => a.ChapterId == chapterId)
            .ToListAsync(ct);

        var existingMap = assignmentsInChapter.ToDictionary(a => a.Id, a => a.GetDependsOnAssignmentIds());

        foreach (var depId in depList)
        {
            if (!existingMap.ContainsKey(depId))
                return "Assignment phụ thuộc không tồn tại trong Chapter này.";
        }

        if (selfId.HasValue)
        {
            var queue = new Queue<Guid>(depList);
            var visited = new HashSet<Guid>(depList);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (current == selfId.Value)
                    return "Dependency tạo chu trình trong đồ thị công việc.";

                if (existingMap.TryGetValue(current, out var nextDeps))
                {
                    foreach (var next in nextDeps)
                    {
                        if (next == selfId.Value)
                            return "Dependency tạo chu trình trong đồ thị công việc.";

                        if (visited.Add(next))
                            queue.Enqueue(next);
                    }
                }
            }
        }

        return null;
    }

    /// <summary>Backward compatibility cho dependency đơn.</summary>
    public static Task<string?> ValidateDependencyAsync(
        IPanelForgeDbContext db, Guid chapterId, Guid? selfId, Guid? dependsOnId, CancellationToken ct) =>
        ValidateDependenciesAsync(db, chapterId, selfId, dependsOnId.HasValue ? [dependsOnId.Value] : null, ct);
}
