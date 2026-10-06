using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Assignments.Common;

/// <summary>Luật chung của cổng duyệt (CF3 bước 2, 6, 7; BR-06, BR-12). Quyền theo vai trò do controller kiểm.</summary>
internal static class ReviewRules
{
    /// <summary>
    /// Tải việc cần duyệt. Lỗi: không thuộc Chapter (NotFound), người duyệt chính là người nhận (Forbidden —
    /// không ai tự duyệt bài mình nộp), Chapter không còn InProduction hoặc việc sai trạng thái (Conflict).
    /// </summary>
    public static async Task<Result<Assignment>> LoadAsync(
        IPanelForgeDbContext db, Guid chapterId, Guid assignmentId, Guid reviewerId,
        AssignmentStatus requiredStatus, CancellationToken ct)
    {
        var assignment = await db.Assignments
            .FirstOrDefaultAsync(a => a.Id == assignmentId && a.ChapterId == chapterId, ct);
        if (assignment is null)
            return Result<Assignment>.Failure($"Assignment {assignmentId} không tồn tại.", ResultErrorCodes.NotFound);

        if (assignment.AssigneeUserId == reviewerId)
            return Result<Assignment>.Failure("Không thể tự duyệt việc do chính mình được giao.", ResultErrorCodes.Forbidden);

        var chapter = await db.Chapters.AsNoTracking().FirstAsync(c => c.Id == chapterId, ct);
        if (AssignmentRules.ChapterNotEditableReason(chapter) is { } locked)
            return Result<Assignment>.Failure(locked, ResultErrorCodes.Conflict);

        if (assignment.Status != requiredStatus)
            return Result<Assignment>.Failure(
                $"Việc đang ở trạng thái {assignment.Status}, cần {requiredStatus} để thực hiện thao tác này.", ResultErrorCodes.Conflict);

        return Result<Assignment>.Success(assignment);
    }
}
