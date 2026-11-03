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

        // Cảnh báo (không chặn Approve): script chưa đóng băng, đề xuất AI còn chờ quyết định (sẽ không Accept được sau khi duyệt)
        if (!chapter.IsScriptFrozen) items.Add(new("SCRIPT_NOT_FROZEN", false, 0, "Kịch bản chưa được đóng băng (freeze-script)."));
        var pendingAi = await db.AiSuggestions.CountAsync(s => s.ChapterId == chapter.Id && s.Status == AiSuggestionStatus.Pending, ct);
        Warn("PENDING_AI_SUGGESTIONS", pendingAi, $"Còn {pendingAi} đề xuất AI chờ quyết định; sau khi duyệt sẽ không Accept được nữa.");

        return items;
    }
}
