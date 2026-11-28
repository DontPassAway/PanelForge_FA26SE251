using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PanelForge.AI.Contracts;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.AiAgents.Models;
using PanelForge.Domain.Entities.Ai;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.AiAgents.Common;

internal static class AiRunHelpers
{
    public static Result<T> Fail<T>(string message, string? code = null) => Result<T>.Failure(message, code);

    /// <summary>
    /// Báo người yêu cầu khi run vừa chuyển sang Completed, Failed hoặc WaitingForHuman (BR-02, UC-14).
    /// Gọi đúng một lần ngay sau Mark*, trước SaveChanges để thông báo lưu cùng giao dịch. Không báo ở từng step;
    /// Interrupted (không phải kết quả của người yêu cầu) bỏ qua.
    /// </summary>
    public static void NotifyRequesterOfFinish(IPanelForgeDbContext db, AiWorkflowRun run)
    {
        (NotificationType Type, string Title, string Message)? notice = run.Status switch
        {
            AiRunStatus.Completed => (Type: NotificationType.AiRunCompleted, Title: "Workflow AI đã hoàn tất",
                Message: "Đề xuất đang chờ bạn quyết định."),
            AiRunStatus.WaitingForHuman => (Type: NotificationType.AiRunWaitingForHuman, Title: "Workflow AI cần bạn quyết định",
                Message: "AI dừng lại, cần con người xem xét các điểm còn tồn đọng."),
            AiRunStatus.Failed => (Type: NotificationType.AiRunFailed, Title: "Workflow AI thất bại",
                Message: run.ErrorMessage ?? "Vui lòng thử chạy lại."),
            _ => null
        };
        if (notice is null) return;

        db.Notifications.Add(Notification.Create(
            run.RequestedByUserId, notice.Value.Type, notice.Value.Title, notice.Value.Message, chapterId: run.ChapterId));
    }

    public static RunSnapshot? ReadSnapshot(AiWorkflowRun run) =>
        JsonSerializer.Deserialize<RunSnapshot>(run.ContextSnapshotJson, AiJson.Options);

    public static IReadOnlyList<Guid> ReadGuidList(string json) =>
        JsonSerializer.Deserialize<List<Guid>>(json, AiJson.Options) ?? [];

    public static JsonElement? ToElement(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { using var doc = JsonDocument.Parse(json); return doc.RootElement.Clone(); }
        catch (JsonException) { return null; }
    }

    public static AiStepDto ToDto(AiAgentStep s) => new(
        s.Id, s.StepIndex, s.Agent, s.Round, s.ParentStepId, s.Status, s.Provider, s.Model, s.PromptVersion, s.PromptRef,
        ToElement(s.ParametersJson), ToElement(s.InputJson), ToElement(s.OutputJson),
        s.StartedAtUtc, s.CompletedAtUtc, s.DurationMs, s.PromptTokens, s.CompletionTokens, s.RepairAttempts,
        s.ErrorCode, s.ErrorMessage);

    public static AiDecisionDto ToDto(AiSuggestionDecision d) => new(
        d.Id, d.Decision, d.DecidedByUserId, d.Reason, d.DecidedAtUtc, d.ChapterVersionAtDecision, d.ResultRevisionId,
        d.ModificationLevel);

    /// <param name="viewerRole">Vai trò của người gọi API trong Workspace (null = không phải thành viên) để tính <c>CanDecide</c>.</param>
    public static AiSuggestionDto ToDto(AiSuggestion s, IEnumerable<AiSuggestionDecision> decisions, WorkspaceRole? viewerRole)
    {
        var roles = AiWorkflowPolicy.ReviewRoles(s.Kind);
        return new AiSuggestionDto(
            s.Id, s.RunId, s.StepId, s.SceneId, s.Kind, s.Status, s.Summary, ToElement(s.PayloadJson), s.PayloadHash,
            s.BaseChapterVersion, s.BaseScriptRevisionId, ReadGuidList(s.BibleRevisionIdsJson), s.ParentSuggestionId,
            s.HasBlockingFindings, s.CreatedAt, decisions.OrderBy(d => d.DecidedAtUtc).Select(ToDto).ToList(),
            roles, s.Status == AiSuggestionStatus.Pending && viewerRole is { } r && roles.Contains(r),
            s.Status == AiSuggestionStatus.Pending && viewerRole is { } a && AiWorkflowPolicy.AcceptRoles(s.Kind).Contains(a));
    }

    /// <summary>Lý do "Yêu cầu sửa" của đề xuất cùng Series và Scene; null nếu đề xuất không ở trạng thái đó.</summary>
    public static async Task<string?> RevisionFeedbackAsync(IPanelForgeDbContext db, Guid seriesId, Guid suggestionId, Guid sceneId, CancellationToken ct)
    {
        var valid = await db.AiSuggestions.AsNoTracking().AnyAsync(s => s.Id == suggestionId && s.SeriesId == seriesId
            && s.SceneId == sceneId && s.Status == AiSuggestionStatus.ChangesRequested, ct);
        if (!valid) return null;
        return await db.AiSuggestionDecisions.AsNoTracking()
            .Where(d => d.SuggestionId == suggestionId && d.Decision == AiSuggestionStatus.ChangesRequested)
            .OrderByDescending(d => d.DecidedAtUtc).Select(d => d.Reason).FirstOrDefaultAsync(ct);
    }

    /// <summary>Ghép lý do của người duyệt vào đầu vào, ưu tiên giữ lý do khi vượt giới hạn.</summary>
    public static string AppendFeedback(string? input, string feedback, int maxLength)
    {
        var note = $"Yêu cầu sửa từ người duyệt: {feedback.Trim()}";
        if (note.Length >= maxLength) return note[..maxLength];
        if (string.IsNullOrWhiteSpace(input)) return note;
        var room = maxLength - note.Length - 2;
        var kept = input.Trim();
        return (kept.Length > room ? kept[..room] : kept) + "\n\n" + note;
    }

    /// <summary>Quyền đọc: thành viên Workspace chứa Series (BR-07, BR-23). Run/suggestion phải thuộc đúng Series trên route.</summary>
    public static async Task<Guid?> WorkspaceOfSeriesAsync(IPanelForgeDbContext db, Guid seriesId, CancellationToken ct) =>
        await db.Series.AsNoTracking().Where(s => s.Id == seriesId).Select(s => (Guid?)s.WorkspaceId).FirstOrDefaultAsync(ct);
}