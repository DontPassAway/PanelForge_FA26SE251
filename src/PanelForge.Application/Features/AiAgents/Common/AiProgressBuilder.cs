using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PanelForge.AI.Contracts;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.AiAgents.Common;

/// <summary>
/// Dựng snapshot tiến độ cho ProducerAgent. MỌI con số (tổng, đếm theo trạng thái, phần trăm, số ngày trễ, điểm nghẽn) do CODE tính ở đây;
/// agent chỉ được diễn giải và trích khoá F1..Fn. Thiếu dữ liệu thì ghi vào DataGaps thay vì để agent đoán.
/// Không đưa tên/email thành viên ra khỏi hệ thống: chỉ dùng vai trò + stage.
/// </summary>
internal static class AiProgressBuilder
{
    private const int MaxListed = 10;
    private const int SoonDays = 3;

    public static async Task<ProgressContext> BuildAsync(IPanelForgeDbContext db, Chapter chapter, DateTime nowUtc, CancellationToken ct)
    {
        var assignments = await db.Assignments.AsNoTracking()
            .Where(a => a.SeriesId == chapter.SeriesId && a.ChapterId == chapter.Id).ToListAsync(ct);
        var stageIds = assignments.Select(a => a.PipelineStageId).Distinct().ToList();
        var stageNames = await db.PipelineStages.AsNoTracking().Where(s => stageIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.Name, ct);

        var scenes = await db.Scenes.AsNoTracking().Where(s => s.ChapterId == chapter.Id).Select(s => s.Id).ToListAsync(ct);
        var scriptLines = await db.ScriptLines.AsNoTracking().CountAsync(l => scenes.Contains(l.SceneId), ct);
        var pageIds = await db.Pages.AsNoTracking().Where(p => p.ChapterId == chapter.Id).Select(p => p.Id).ToListAsync(ct);
        var panels = await db.Panels.AsNoTracking().CountAsync(p => pageIds.Contains(p.PageId), ct);
        var pendingSuggestions = await db.AiSuggestions.AsNoTracking()
            .CountAsync(s => s.ChapterId == chapter.Id && s.Status == AiSuggestionStatus.Pending, ct);

        var facts = new List<ProgressFact>();
        var gaps = new List<string>();
        var percents = new List<string>();
        void Fact(string text) => facts.Add(new ProgressFact($"F{facts.Count + 1}", text));
        string Day(DateTime d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        Fact($"Chương {chapter.ChapterNumber:0.##}{(string.IsNullOrWhiteSpace(chapter.Title) ? "" : $" \"{chapter.Title}\"")}: trạng thái {chapter.Status}; version nội dung {chapter.VersionVector}; số liệu tính lúc {Day(nowUtc)} (UTC).");

        if (chapter.TargetReleaseDate is { } target)
        {
            var left = target.DayNumber - DateOnly.FromDateTime(nowUtc).DayNumber;
            Fact(left >= 0
                ? $"Ngày phát hành mục tiêu {target:yyyy-MM-dd}, còn {left} ngày."
                : $"Ngày phát hành mục tiêu {target:yyyy-MM-dd}, đã quá {-left} ngày.");
        }
        else gaps.Add("Chapter chưa đặt ngày phát hành mục tiêu nên không đánh giá được rủi ro trễ phát hành.");

        if (assignments.Count == 0)
        {
            gaps.Add("Chapter chưa có assignment nào nên không đánh giá được tiến độ giao việc, deadline và điểm nghẽn theo stage.");
        }
        else
        {
            var byStatus = assignments.GroupBy(a => a.Status).OrderBy(g => g.Key)
                .Select(g => $"{g.Key} {g.Count()}");
            Fact($"Assignment: tổng {assignments.Count}; {string.Join(", ", byStatus)}.");

            var approved = assignments.Count(a => a.Status == AssignmentStatus.Approved);
            var pct = (int)Math.Round(approved * 100.0 / assignments.Count, MidpointRounding.AwayFromZero);
            percents.Add($"{pct}%");
            Fact($"Hoàn thành assignment (đã Approved): {approved}/{assignments.Count} = {pct}%.");

            string Label(Domain.Entities.Workflow.Assignment a) =>
                $"{a.AssignedRole}/{(stageNames.TryGetValue(a.PipelineStageId, out var n) ? n : "stage không rõ")}";

            var open = assignments.Where(a => a.Status != AssignmentStatus.Approved).ToList();
            var today = DateOnly.FromDateTime(nowUtc);
            var overdue = open.Where(a => a.DueDate is { } d && DateOnly.FromDateTime(d) < today)
                .OrderBy(a => a.DueDate).ToList();
            foreach (var a in overdue.Take(MaxListed))
                Fact($"Assignment {Label(a)} hạn {Day(a.DueDate!.Value)}, đã quá {today.DayNumber - DateOnly.FromDateTime(a.DueDate!.Value).DayNumber} ngày, trạng thái {a.Status}.");
            if (overdue.Count > MaxListed) Fact($"Còn {overdue.Count - MaxListed} assignment quá hạn khác không liệt kê.");
            if (overdue.Count == 0 && open.Any(a => a.DueDate is not null)) Fact("Không có assignment nào quá hạn.");

            foreach (var a in open.Where(a => a.DueDate is { } d && DateOnly.FromDateTime(d) is var dd
                                              && dd >= today && dd.DayNumber - today.DayNumber <= SoonDays).Take(MaxListed))
                Fact($"Assignment {Label(a)} sắp đến hạn {Day(a.DueDate!.Value)} (trong {SoonDays} ngày), trạng thái {a.Status}.");

            foreach (var a in open.Where(a => a.Status is AssignmentStatus.ReworkRequired or AssignmentStatus.Blocked).Take(MaxListed))
                Fact($"Assignment {Label(a)} đang {a.Status}{(string.IsNullOrWhiteSpace(a.LastFeedbackComment) ? "" : $": \"{Short(a.LastFeedbackComment)}\"")}.");

            var bottleneck = open.GroupBy(a => a.PipelineStageId).OrderByDescending(g => g.Count()).FirstOrDefault();
            if (bottleneck is not null)
                Fact($"Điểm nghẽn theo stage: stage \"{(stageNames.TryGetValue(bottleneck.Key, out var bn) ? bn : "không rõ")}\" đang giữ {bottleneck.Count()} assignment chưa Approved (nhiều nhất).");

            var noDue = assignments.Count(a => a.DueDate is null && a.Status != AssignmentStatus.Approved);
            if (noDue > 0) gaps.Add($"{noDue} assignment chưa Approved không có deadline nên không đánh giá được trễ hạn.");
        }

        Fact($"Nội dung: {scenes.Count} scene, {scriptLines} dòng script, {pageIds.Count} page, {panels} panel.");
        if (pageIds.Count == 0) gaps.Add("Chapter chưa có page/panel nên không đo được tiến độ nội dung (layout/vẽ).");
        Fact($"Đề xuất AI đang chờ người quyết định: {pendingSuggestions}.");

        return new ProgressContext(
            $"Chương {chapter.ChapterNumber:0.##} (Series {chapter.SeriesId.ToString()[..8]})", nowUtc, facts, gaps, percents);
    }

    private static string Short(string s) => s.Length <= 80 ? s : s[..80] + "…";
}
