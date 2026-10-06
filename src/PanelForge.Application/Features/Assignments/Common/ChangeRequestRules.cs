using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Assignments.Models;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Assignments.Common;

/// <summary>Luật và truy vấn dùng chung của Submission / ChangeRequest (CF3 bước 7–9; BR-06, BR-12).</summary>
internal static class ChangeRequestRules
{
    /// <summary>Tải ChangeRequest thuộc Chapter trên route (chặn IDOR) và bảo đảm Chapter còn InProduction (BR-12).</summary>
    public static async Task<Result<ChangeRequest>> LoadAsync(
        IPanelForgeDbContext db, Guid chapterId, Guid changeRequestId, CancellationToken ct)
    {
        var cr = await db.ChangeRequests.FirstOrDefaultAsync(c => c.Id == changeRequestId && c.ChapterId == chapterId, ct);
        if (cr is null)
            return Result<ChangeRequest>.Failure($"ChangeRequest {changeRequestId} không tồn tại.", ResultErrorCodes.NotFound);

        var chapter = await db.Chapters.AsNoTracking().FirstAsync(c => c.Id == chapterId, ct);
        if (AssignmentRules.ChapterNotEditableReason(chapter) is { } locked)
            return Result<ChangeRequest>.Failure(locked, ResultErrorCodes.Conflict);

        return Result<ChangeRequest>.Success(cr);
    }

    /// <summary>
    /// Số yêu cầu sửa chưa đóng của việc. Nộp lại chỉ cần hết <c>Open</c>; duyệt cổng cần hết cả <c>Open</c> lẫn
    /// <c>Resolved</c> (Editor phải xác minh trước), nên <paramref name="includeResolved"/> = true.
    /// </summary>
    public static Task<int> UnclosedCountAsync(
        IPanelForgeDbContext db, Guid assignmentId, bool includeResolved, CancellationToken ct) =>
        db.ChangeRequests.CountAsync(c => c.AssignmentId == assignmentId &&
            (c.Status == ChangeRequestStatus.Open || (includeResolved && c.Status == ChangeRequestStatus.Resolved)), ct);

    /// <summary>Ghi lần nộp mới (cùng lần Save với việc nộp); bản còn Pending trước đó thành Superseded.</summary>
    public static async Task RecordSubmissionAsync(
        IPanelForgeDbContext db, Guid assignmentId, Guid chapterId, Guid submittedByUserId, CancellationToken ct)
    {
        var previous = await db.Submissions.Where(s => s.AssignmentId == assignmentId).ToListAsync(ct);
        foreach (var pending in previous.Where(s => s.Status == SubmissionStatus.Pending)) pending.Supersede();

        db.Submissions.Add(Submission.Create(assignmentId, chapterId, submittedByUserId, previous.Count + 1));
    }

    /// <summary>Kiểm các yêu cầu sửa gửi kèm lúc trả về. Trả thông báo lỗi hoặc null nếu hợp lệ.</summary>
    public static string? ValidateItems(IReadOnlyList<ChangeRequestItemRequest>? items)
    {
        foreach (var item in items ?? [])
        {
            if (string.IsNullOrWhiteSpace(item.Description)) return "Mỗi yêu cầu sửa phải có mô tả.";
            if (ChangeRequest.ValidateRegion(item.RegionJson) is { } regionError) return regionError;
        }

        return null;
    }

    /// <summary>
    /// Tạo ChangeRequest khi trả về: mỗi phần tử của <paramref name="items"/>, hoặc một yêu cầu lấy từ chính comment nếu không có.
    /// Người chịu trách nhiệm là người nhận việc.
    /// </summary>
    public static void AddChangeRequests(
        IPanelForgeDbContext db, Assignment a, Guid? submissionId, Guid reviewerUserId,
        IReadOnlyList<ChangeRequestItemRequest>? items, string comment)
    {
        IReadOnlyList<ChangeRequestItemRequest> list = items is { Count: > 0 } ? items : [new ChangeRequestItemRequest(comment)];
        foreach (var item in list)
        {
            db.ChangeRequests.Add(ChangeRequest.Create(
                a.Id, a.ChapterId, submissionId, a.AssigneeUserId, reviewerUserId,
                item.Description, item.RegionJson, item.DueDate));
        }
    }

    /// <summary>Lần nộp đang chờ quyết định của việc (mới nhất); null với dữ liệu nộp trước khi có Submission.</summary>
    public static Task<Submission?> LatestPendingSubmissionAsync(
        IPanelForgeDbContext db, Guid assignmentId, CancellationToken ct) =>
        db.Submissions
            .Where(s => s.AssignmentId == assignmentId && s.Status == SubmissionStatus.Pending)
            .OrderByDescending(s => s.AttemptNumber)
            .FirstOrDefaultAsync(ct);
}
