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
    /// Dependency phải cùng Chapter và không tạo chu trình. <paramref name="selfId"/> null khi đang tạo mới
    /// (việc mới chưa có ai phụ thuộc nên không thể thành chu trình).
    /// Trả về thông báo lỗi, hoặc null nếu hợp lệ.
    /// </summary>
    public static async Task<string?> ValidateDependencyAsync(
        IPanelForgeDbContext db, Guid chapterId, Guid? selfId, Guid? dependsOnId, CancellationToken ct)
    {
        if (dependsOnId is null) return null;

        var edges = (await db.Assignments.AsNoTracking()
                .Where(a => a.ChapterId == chapterId)
                .Select(a => new { a.Id, a.DependsOnAssignmentId })
                .ToListAsync(ct))
            .ToDictionary(a => a.Id, a => a.DependsOnAssignmentId);

        if (!edges.ContainsKey(dependsOnId.Value))
            return "Assignment phụ thuộc không tồn tại trong Chapter này.";

        var seen = new HashSet<Guid>();
        for (Guid? cur = dependsOnId; cur is { } id && seen.Add(id); cur = edges.GetValueOrDefault(id))
        {
            if (id == selfId) return "Dependency tạo chu trình.";
        }

        return null;
    }
}
