using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Chapters.Common;

/// <summary>Một điều kiện của cổng duyệt/khoá chương. Blocking = ApproveChapter sẽ từ chối; còn lại chỉ là cảnh báo.</summary>
public sealed record ChapterLockGateItem(string Code, bool Blocking, int Count, string Message);

public sealed record ChapterLockGateDto(Guid ChapterId, ChapterStatus Status, bool CanApprove, IReadOnlyList<ChapterLockGateItem> Items);

/// <summary>
/// Cổng tất định trước khi duyệt/khoá chương (BR-12, CF3 Consistency gate) — KHÔNG dùng LLM.
/// MỘT nguồn luật cho ApproveChapterCommand (chặn thật) và GetChapterLockGateQuery (liệt kê cho Producer), nên hai nơi không lệch nhau.
/// </summary>
public static class ChapterLockGate
{
    public static async Task<List<ChapterLockGateItem>> EvaluateAsync(IPanelForgeDbContext db, Chapter chapter, CancellationToken ct)
    {
        var items = new List<ChapterLockGateItem>();
        void Block(string code, int count, string message) { if (count > 0) items.Add(new(code, true, count, message)); }
        void Warn(string code, int count, string message) { if (count > 0) items.Add(new(code, false, count, message)); }

        if (!await db.Pages.AnyAsync(p => p.ChapterId == chapter.Id, ct))
            items.Add(new("NO_PAGES", true, 0, "Chapter chưa có Page nào, chưa thể duyệt."));

        var unfinished = await db.Assignments.CountAsync(a => a.ChapterId == chapter.Id && a.Status != AssignmentStatus.Approved, ct);
        Block("ASSIGNMENTS_NOT_APPROVED", unfinished, $"Còn {unfinished} việc chưa được duyệt, chưa thể duyệt Chapter.");

        var openRequests = await db.ChangeRequests.CountAsync(c => c.ChapterId == chapter.Id &&
            (c.Status == ChangeRequestStatus.Open || c.Status == ChangeRequestStatus.Resolved), ct);
        Block("OPEN_CHANGE_REQUESTS", openRequests, $"Còn {openRequests} yêu cầu sửa chưa đóng, chưa thể duyệt Chapter.");

        var blockingFindings = await db.ConsistencyFindings.CountAsync(f => f.ChapterId == chapter.Id &&
            f.Severity == ConsistencyFindingSeverity.Blocking &&
            // Finding ảnh kiểu cũ dùng điểm giả (hash) → không được chặn duyệt chương
            f.RuleCode != PanelForge.Application.Features.Consistency.Common.ConsistencyChecker.VisualCharacterDeviation &&
            (f.Status == ConsistencyFindingStatus.Open || f.Status == ConsistencyFindingStatus.Confirmed ||
             f.Status == ConsistencyFindingStatus.NeedsClarification), ct);
        Block("BLOCKING_FINDINGS", blockingFindings, $"Còn {blockingFindings} lỗi nhất quán mức Blocking chưa xử lý, chưa thể duyệt Chapter.");

        // BR-12: cờ ảnh hưởng ⚑ chưa quyết định Keep/Rework. FlagImpactReview giữ nguyên trạng thái Approved của việc,
        // nên ASSIGNMENTS_NOT_APPROVED không bắt được trường hợp này.
        var chapterPanelIds = db.Panels.Where(p => db.Pages.Any(pg => pg.Id == p.PageId && pg.ChapterId == chapter.Id)).Select(p => p.Id);
        var impactFlags =
            await db.Assignments.CountAsync(a => a.ChapterId == chapter.Id && (a.ImpactReviewRequestedAt != null || a.HasScriptImpact), ct) +
            await db.StageProgresses.CountAsync(sp => chapterPanelIds.Contains(sp.PanelId) && sp.HasScriptImpact, ct);
        Block("IMPACT_FLAGS", impactFlags, $"Còn {impactFlags} cờ ảnh hưởng ⚑ chưa quyết định Keep/Rework, chưa thể duyệt Chapter.");

        // Trang chưa từng được giao cho Artist/Letterer thì không có lượt duyệt Gate 2 nào. Đang để cảnh báo vì luồng cũ
        // vẫn cho phép duyệt chương không qua giao việc; chuyển thành Block khi Gate 1 tự giao Page (P1-02).
        var unassignedPages = await PagesWithoutProductionWorkAsync(db, chapter.Id, ct);
        if (unassignedPages.Count > 0)
            items.Add(new("PAGES_WITHOUT_WORK", false, unassignedPages.Count,
                $"Trang {string.Join(", ", unassignedPages)} chưa được giao cho Artist/Letterer nên chưa qua duyệt Gate 2."));

        // Cảnh báo (không chặn Approve): script chưa đóng băng, đề xuất AI còn chờ quyết định (sẽ không Accept được sau khi duyệt)
        if (!chapter.IsScriptFrozen) items.Add(new("SCRIPT_NOT_FROZEN", false, 0, "Kịch bản chưa được đóng băng (freeze-script)."));
        var pendingAi = await db.AiSuggestions.CountAsync(s => s.ChapterId == chapter.Id && s.Status == AiSuggestionStatus.Pending, ct);
        Warn("PENDING_AI_SUGGESTIONS", pendingAi, $"Còn {pendingAi} đề xuất AI chờ quyết định; sau khi duyệt sẽ không Accept được nữa.");

        return items;
    }

    private static async Task<List<int>> PagesWithoutProductionWorkAsync(IPanelForgeDbContext db, Guid chapterId, CancellationToken ct)
    {
        var work = await db.Assignments.AsNoTracking()
            .Where(a => a.ChapterId == chapterId &&
                        (a.AssignedRole == WorkspaceRole.Artist || a.AssignedRole == WorkspaceRole.Letterer))
            .Select(a => new { a.EntityType, a.TargetEntityId })
            .ToListAsync(ct);
        if (work.Any(a => a.EntityType == WorkflowEntityType.Chapter)) return [];

        var panelTargets = work.Where(a => a.EntityType == WorkflowEntityType.Panel).Select(a => a.TargetEntityId).ToList();
        var covered = work.Where(a => a.EntityType == WorkflowEntityType.Page).Select(a => a.TargetEntityId).ToHashSet();
        covered.UnionWith(await db.Panels.AsNoTracking().Where(p => panelTargets.Contains(p.Id)).Select(p => p.PageId).ToListAsync(ct));

        return await db.Pages.AsNoTracking()
            .Where(p => p.ChapterId == chapterId && !covered.Contains(p.Id))
            .OrderBy(p => p.PageNumber).Select(p => p.PageNumber)
            .ToListAsync(ct);
    }
}
