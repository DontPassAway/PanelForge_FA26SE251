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

    /// <summary>
    /// Chuyển giao: yêu cầu sửa còn mở (Open/Resolved) của <paramref name="from"/> sang việc <paramref name="to"/> và người nhận
    /// của nó. <paramref name="panelIds"/> null = mọi yêu cầu (đổi người cả việc); khác null = chỉ yêu cầu gắn Panel trong phạm vi
    /// được chuyển (yêu cầu không gắn Panel ở lại việc cũ). Yêu cầu đã đóng ở lại làm lịch sử. Trả số yêu cầu đã chuyển.
    /// </summary>
    public static async Task<int> TransferOpenAsync(
        IPanelForgeDbContext db, Assignment from, Assignment to, IReadOnlySet<Guid>? panelIds, CancellationToken ct)
    {
        var open = await db.ChangeRequests
            .Where(c => c.AssignmentId == from.Id &&
                        (c.Status == ChangeRequestStatus.Open || c.Status == ChangeRequestStatus.Resolved))
            .ToListAsync(ct);
        var moved = 0;
        foreach (var cr in open.Where(c => panelIds is null || (c.PanelId is { } p && panelIds.Contains(p))))
        {
            cr.TransferTo(to.Id, to.AssigneeUserId);
            moved++;
        }

        return moved;
    }

    /// <summary>Ghi lần nộp mới (cùng lần Save với việc nộp); bản còn Pending trước đó thành Superseded.</summary>
    public static async Task RecordSubmissionAsync(
        IPanelForgeDbContext db, Guid assignmentId, Guid chapterId, Guid submittedByUserId, CancellationToken ct,
        IReadOnlyList<Guid>? artworkReferenceIds = null, long chapterVersion = 0, string? panelLayoutJson = null,
        string? canvasVersionsJson = null, IReadOnlyList<Guid>? scriptRevisionIds = null)
    {
        var previous = await db.Submissions.Where(s => s.AssignmentId == assignmentId).ToListAsync(ct);
        foreach (var pending in previous.Where(s => s.Status == SubmissionStatus.Pending)) pending.Supersede();

        db.Submissions.Add(Submission.Create(
            assignmentId, chapterId, submittedByUserId, previous.Count + 1, artworkReferenceIds, chapterVersion, panelLayoutJson,
            canvasVersionsJson, scriptRevisionIds));
    }

    /// <summary>
    /// Bản artwork đang nộp: phiên bản mới nhất của từng (Page, Panel) thuộc các trang việc bao phủ, ở stage của việc
    /// (ảnh cả trang có PanelId null). Ảnh của Panel không còn trong <paramref name="alivePanelIds"/> (đã bị xoá) bị bỏ,
    /// vì nó không còn thuộc bố cục được nộp. Việc cấp Chapter / không có artwork → rỗng.
    /// </summary>
    public static async Task<List<Guid>> LatestArtworkIdsAsync(
        IPanelForgeDbContext db, Guid stageId, IEnumerable<Guid> pageIds, IEnumerable<Guid> alivePanelIds, CancellationToken ct)
    {
        var pages = pageIds.Distinct().ToList();
        if (pages.Count == 0) return [];
        var alive = alivePanelIds.ToHashSet();
        var refs = (await db.AssetReferences.AsNoTracking()
            .Where(r => r.PipelineStageId == stageId && pages.Contains(r.PageId))
            .Select(r => new { r.Id, r.PageId, r.PanelId, r.VersionNumber }).ToListAsync(ct))
            .Where(r => r.PanelId is null || alive.Contains(r.PanelId.Value));
        return refs.GroupBy(r => (r.PageId, r.PanelId))
            .Select(g => g.OrderByDescending(r => r.VersionNumber).First().Id).ToList();
    }

    /// <summary>Kiểm các yêu cầu sửa gửi kèm lúc trả về. Trả thông báo lỗi hoặc null nếu hợp lệ.</summary>
    public static string? ValidateItems(IReadOnlyList<ChangeRequestItemRequest>? items)
    {
        foreach (var item in items ?? [])
        {
            if (string.IsNullOrWhiteSpace(item.Description)) return "Mỗi yêu cầu sửa phải có mô tả.";
            if (ChangeRequest.ValidateRegion(item.RegionJson) is { } regionError) return regionError;
            if (item.ScriptLineId is not null && item.SceneId is null)
                return "Nhận xét dòng kịch bản phải kèm phân cảnh chứa dòng đó.";
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
                item.Description, item.RegionJson, item.DueDate, item.AssetReferenceId, item.PanelId,
                item.SceneId, item.ScriptLineId, item.QuotedText));
        }
    }

    /// <summary>
    /// Chống quyết định nhầm lần nộp (CF3: duyệt gắn với đúng bản): Editor gửi id lần nộp đang xem; nếu khác lần nộp đang chờ
    /// (đã có người khác quyết định / đã nộp lại) thì trả thông báo lỗi. Không gửi = không kiểm (client cũ).
    /// </summary>
    public static async Task<string?> SubmissionMismatchAsync(
        IPanelForgeDbContext db, Guid assignmentId, Guid? expectedSubmissionId, CancellationToken ct)
    {
        if (expectedSubmissionId is null) return null;
        var pending = await LatestPendingSubmissionAsync(db, assignmentId, ct);
        return pending?.Id == expectedSubmissionId
            ? null
            : "Lần nộp đang xem không còn là lần nộp chờ duyệt (đã có quyết định hoặc lần nộp mới), hãy tải lại.";
    }

    /// <summary>Lần nộp đang chờ quyết định của việc (mới nhất); null với dữ liệu nộp trước khi có Submission.</summary>
    public static Task<Submission?> LatestPendingSubmissionAsync(
        IPanelForgeDbContext db, Guid assignmentId, CancellationToken ct) =>
        db.Submissions
            .Where(s => s.AssignmentId == assignmentId && s.Status == SubmissionStatus.Pending)
            .OrderByDescending(s => s.AttemptNumber)
            .FirstOrDefaultAsync(ct);
}
