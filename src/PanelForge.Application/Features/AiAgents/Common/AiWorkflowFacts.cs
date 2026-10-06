using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PanelForge.AI.Contracts;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.AiAgents.Models;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.AiAgents.Common;

/// <summary>Panel của một layout đã áp dụng, đọc từ mapping của ContentRevision(Layout): layout (page, panel) ↔ Page/Panel thật trong DB.</summary>
internal sealed record MappedPanel(
    int LayoutPageNumber, int PageNumber, Guid PageId, int PanelNumber, Guid PanelId, IReadOnlyList<Guid> SourceScriptLineIds);

internal static class AiLayoutMapping
{
    public static IReadOnlyList<MappedPanel> Read(string mappingJson)
    {
        var result = new List<MappedPanel>();
        try
        {
            using var doc = JsonDocument.Parse(mappingJson);
            if (!doc.RootElement.TryGetProperty("pages", out var pages) || pages.ValueKind != JsonValueKind.Array) return result;
            foreach (var page in pages.EnumerateArray())
            {
                var layoutPage = page.GetProperty("layoutPageNumber").GetInt32();
                var pageNumber = page.GetProperty("pageNumber").GetInt32();
                var pageId = page.GetProperty("pageId").GetGuid();
                foreach (var panel in page.GetProperty("panels").EnumerateArray())
                {
                    var lines = panel.GetProperty("sourceScriptLineIds").EnumerateArray().Select(e => e.GetGuid()).ToList();
                    result.Add(new MappedPanel(layoutPage, pageNumber, pageId,
                        panel.GetProperty("panelNumber").GetInt32(), panel.GetProperty("panelId").GetGuid(), lines));
                }
            }
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return [];   // mapping hỏng → coi như không có layout dùng được; không đoán
        }
        return result;
    }
}

/// <summary>Kết quả kiểm tra assignment của người gọi trong Chapter (vai trò khác Producer).</summary>
internal sealed record AssignmentGate(bool RoleHasAssignments, string? BlockedReason)
{
    public static readonly AssignmentGate Open = new(false, null);
}

/// <summary>
/// Dữ kiện do CODE đọc từ DB để quyết định ai được chạy workflow nào và còn thiếu điều kiện gì:
/// membership + vai trò, AI bật cho Workspace, trạng thái Chapter (khoá), assignment của người gọi, và context Scene.
/// Dùng chung cho RunWorkflow (chặn thật) và API availability (báo cho Frontend) nên hai nơi không thể lệch nhau.
/// </summary>
internal sealed record AiWorkflowFacts(
    Guid WorkspaceId,
    WorkspaceRole? Role,
    bool AiEnabled,
    ChapterStatus ChapterStatus,
    bool HasBible,
    int ScriptLineCount,
    int DialogueLineCount,
    Guid? LatestLayoutRevisionId,
    int LayoutPanelsAlive,
    int ScenePageCount,
    AssignmentGate Assignment)
{
    private static readonly AssignmentStatus[] WorkingStatuses =
        [AssignmentStatus.Assigned, AssignmentStatus.InProgress, AssignmentStatus.ReworkRequired];

    public static async Task<AiWorkflowFacts> LoadAsync(
        IPanelForgeDbContext db, IWorkspaceAuthorizationService auth, Guid userId, Guid workspaceId,
        Chapter chapter, Scene? scene, CancellationToken ct)
    {
        var role = await auth.GetMemberRoleAsync(userId, workspaceId, ct);
        var aiEnabled = await db.WorkspaceAiConfigs.AsNoTracking().AnyAsync(c => c.WorkspaceId == workspaceId && c.IsEnabled, ct);
        var hasBible = await db.SeriesBibles.AsNoTracking().AnyAsync(b => b.SeriesId == chapter.SeriesId, ct);

        int lines = 0, dialogue = 0, pages = 0, alive = 0;
        Guid? layoutRevisionId = null;
        if (scene is not null)
        {
            lines = await db.ScriptLines.AsNoTracking().CountAsync(l => l.SceneId == scene.Id, ct);
            dialogue = await db.ScriptLines.AsNoTracking().CountAsync(
                l => l.SceneId == scene.Id && l.DialogueText != null && l.DialogueText != "", ct);
            pages = await db.Panels.AsNoTracking().Where(p => p.SceneId == scene.Id).Select(p => p.PageId).Distinct().CountAsync(ct);
            var layout = await LatestLayoutAsync(db, scene.Id, ct);
            if (layout is not null)
            {
                layoutRevisionId = layout.Id;
                var panelIds = AiLayoutMapping.Read(layout.MappingJson).Select(p => p.PanelId).ToList();
                alive = await db.Panels.AsNoTracking().CountAsync(p => panelIds.Contains(p.Id), ct);
            }
        }

        var gate = role is null or WorkspaceRole.Producer
            ? AssignmentGate.Open
            : await EvaluateAssignmentAsync(db, userId, chapter, role.Value, ct);

        return new AiWorkflowFacts(workspaceId, role, aiEnabled, chapter.Status, hasBible, lines, dialogue,
            layoutRevisionId, alive, pages, gate);
    }

    public static Task<ContentRevision?> LatestLayoutAsync(IPanelForgeDbContext db, Guid sceneId, CancellationToken ct) =>
        db.ContentRevisions.AsNoTracking()
            .Where(r => r.SceneId == sceneId && r.Kind == ContentRevisionKind.Layout)
            .OrderByDescending(r => r.VersionNumber).FirstOrDefaultAsync(ct);

    /// <summary>
    /// Nếu Chapter đã giao việc cho vai trò này thì người gọi phải là người được giao và assignment phải đang ở trạng thái làm việc,
    /// stage hiện tại phải dành cho vai trò đó. Chapter chưa giao việc cho vai trò này → việc mở cho mọi thành viên cùng vai trò.
    /// </summary>
    private static async Task<AssignmentGate> EvaluateAssignmentAsync(
        IPanelForgeDbContext db, Guid userId, Chapter chapter, WorkspaceRole role, CancellationToken ct)
    {
        var all = await db.Assignments.AsNoTracking()
            .Where(a => a.SeriesId == chapter.SeriesId && a.ChapterId == chapter.Id && a.AssignedRole == role)
            .ToListAsync(ct);
        if (all.Count == 0) return AssignmentGate.Open;

        var mine = all.Where(a => a.AssigneeUserId == userId).ToList();
        if (mine.Count == 0)
            return new AssignmentGate(true, $"Chapter đã giao việc vai trò {role} cho thành viên khác; bạn chưa được giao assignment nào.");

        var working = mine.Where(a => WorkingStatuses.Contains(a.Status)).ToList();
        if (working.Count == 0)
            return new AssignmentGate(true,
                $"Assignment của bạn không ở trạng thái làm việc (hiện: {string.Join(", ", mine.Select(a => a.Status).Distinct())}).");

        var stageIds = working.Select(a => a.PipelineStageId).Distinct().ToList();
        var stages = await db.PipelineStages.AsNoTracking().Where(s => stageIds.Contains(s.Id)).ToListAsync(ct);
        // Stage không đọc được (không xảy ra khi FK còn nguyên) thì không đủ căn cứ để chặn; stage có AllowedRole khác vai trò → chặn
        var stageOk = working.Any(a => stages.FirstOrDefault(s => s.Id == a.PipelineStageId) is not { } s
                                       || s.AllowedRole is null || s.AllowedRole == role);
        return stageOk
            ? new AssignmentGate(true, null)
            : new AssignmentGate(true, $"Stage hiện tại của assignment không dành cho vai trò {role}.");
    }

    /// <summary>Danh sách điều kiện còn thiếu cho một loại workflow; rỗng = chạy được. Thứ tự: quyền → cấu hình → trạng thái → context.</summary>
    public IReadOnlyList<AiMissingConditionDto> Evaluate(AiWorkflowType type)
    {
        var missing = new List<AiMissingConditionDto>();
        void Add(string code, string kind, string message) => missing.Add(new AiMissingConditionDto(code, kind, message));

        var allowed = AiWorkflowPolicy.RunRoles(type);
        if (Role is null)
        {
            Add("NOT_MEMBER", "Permission", "Bạn không phải thành viên của Workspace này.");
            return missing;
        }
        if (!allowed.Contains(Role.Value))
            Add("ROLE_NOT_ALLOWED", "Permission",
                $"Vai trò {Role} không được chạy nhiệm vụ này (cần: {string.Join("/", allowed)}).");
        else if (Assignment.BlockedReason is not null)
            Add("ASSIGNMENT_REQUIRED", "Permission", Assignment.BlockedReason);

        if (!AiEnabled) Add("AI_DISABLED", "Config", "Tính năng AI chưa được bật cho Workspace này.");

        if (AiWorkflowPolicy.ProducesApplicableSuggestions(type) && ChapterStatus != ChapterStatus.InProduction)
            Add("CHAPTER_LOCKED", "State", $"Chapter đang ở trạng thái {ChapterStatus} (chỉ đọc), không thể tạo đề xuất áp dụng được.");

        var needsBible = type is not AiWorkflowType.Producer and not AiWorkflowType.Letterer;
        if (needsBible && !HasBible) Add("NO_BIBLE", "Context", "Series chưa có Series Bible.");

        switch (type)
        {
            case AiWorkflowType.Storyboard:
                if (ScriptLineCount == 0) Add("NO_SCRIPT", "Context", "Scene chưa có script. Chạy workflow Script và Accept trước.");
                if (ScenePageCount > 0) Add("SCENE_HAS_PAGES", "State", "Scene đã có Page; layout AI chỉ áp dụng vào Scene chưa có Page.");
                break;
            case AiWorkflowType.Editor:
                if (ScriptLineCount == 0) Add("NO_SCRIPT", "Context", "Scene chưa có script để review.");
                break;
            case AiWorkflowType.Artist:
                if (LatestLayoutRevisionId is null) Add("NO_LAYOUT", "Context", "Scene chưa có layout đã Accept. Chạy Storyboard và Accept layout trước.");
                else if (LayoutPanelsAlive == 0) Add("NO_PANELS", "Context", "Các Panel của layout đã Accept không còn tồn tại.");
                break;
            case AiWorkflowType.Letterer:
                if (DialogueLineCount == 0) Add("NO_DIALOGUE", "Context", "Scene chưa có dòng thoại nào.");
                if (LatestLayoutRevisionId is null) Add("NO_LAYOUT", "Context", "Scene chưa có layout đã Accept để biết thoại nằm ở panel nào.");
                else if (LayoutPanelsAlive == 0) Add("NO_PANELS", "Context", "Các Panel của layout đã Accept không còn tồn tại.");
                break;
        }
        return missing;
    }
}
