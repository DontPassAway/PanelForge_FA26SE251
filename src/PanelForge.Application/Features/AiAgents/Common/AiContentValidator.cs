using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PanelForge.AI.Contracts;
using PanelForge.Application.Features.AiAgents.Models;
using PanelForge.Domain.Entities.Ai;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.AiAgents.Common;

/// <summary>
/// Kiểm tra lại output của agent phía Backend (không tin dữ liệu từ model hay từ AI service): dùng cùng
/// <see cref="ContentRules"/> với AI service nhưng scope (Bible, speaker, revision IDs) lấy từ snapshot đã lưu ở đầu run.
/// </summary>
public static class AiContentValidator
{
    public static T? TryParse<T>(string? json, out string? error) where T : class
    {
        error = null;
        if (string.IsNullOrWhiteSpace(json)) { error = "Output rỗng."; return null; }
        try
        {
            var value = AiJson.Deserialize<T>(json);
            if (value is null) error = "Output không phải JSON object hợp lệ.";
            return value;
        }
        catch (JsonException ex)
        {
            error = $"JSON không hợp lệ: {ex.Message}";
            return null;
        }
    }

    public static string Sha256(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    public static IReadOnlyList<string> ValidateScript(AiWorkflowRun run, RunSnapshot snap, ScriptDraft draft) =>
        ContentRules.ValidateScript(draft, run.Id, run.SceneId ?? Guid.Empty, CharacterIds(snap), RevisionIds(snap));

    /// <summary>Script nền khi workflow không có Writer (Storyboard/Editor): script có sẵn của Scene lúc chụp snapshot, coi như lượt 0.</summary>
    public static ScriptDraft BaselineDraft(AiWorkflowRun run, RunSnapshot snap) =>
        new(AiProtocol.SchemaVersion, run.Id, run.SceneId ?? Guid.Empty, 0,
            snap.BaselineScript.OrderBy(l => l.Order)
                .Select(l => new ScriptDraftLine(l.LineId, l.Order, l.SpeakerId, l.Dialogue, l.StageDirection)).ToList(),
            snap.Bible.Select(b => b.RevisionId).ToList(), null);

    public static IReadOnlyList<string> ValidateArtist(AiWorkflowRun run, RunSnapshot snap, ArtistPromptSet set) =>
        snap.Artist is null
            ? ["Run không có context Artist (layout/panel)."]
            : ContentRules.ValidateArtist(set, run.Id, run.SceneId ?? Guid.Empty, snap.Artist, snap.Bible.Select(b => b.EntryId).ToHashSet());

    public static IReadOnlyList<string> ValidateLettering(AiWorkflowRun run, RunSnapshot snap, LetteringSuggestionSet set) =>
        snap.Lettering is null
            ? ["Run không có context Letterer (thoại/panel/bubble)."]
            : ContentRules.ValidateLettering(set, run.Id, run.SceneId ?? Guid.Empty, snap.Lettering);

    public static IReadOnlyList<string> ValidateProduction(AiWorkflowRun run, RunSnapshot snap, ProductionReport report) =>
        snap.Progress is null
            ? ["Run không có snapshot tiến độ."]
            : ContentRules.ValidateProduction(report, run.Id, snap.Progress);

    public static IReadOnlyList<string> ValidateContinuity(
        AiWorkflowRun run, RunSnapshot snap, ContinuityReport report, ScriptDraft script) =>
        ContentRules.ValidateContinuity(report, run.Id, script,
            snap.Bible.ToDictionary(b => b.RevisionId, b => (b.EntryId, $"{b.Name}\n{b.Description}")));

    public static IReadOnlyList<string> ValidateLayout(
        AiWorkflowRun run, RunSnapshot snap, StoryboardLayout layout, ScriptDraft script) =>
        ContentRules.ValidateLayout(layout, run.Id, run.SceneId ?? Guid.Empty, script, run.ReadingDirection.ToString(), CharacterIds(snap));

    public static IReadOnlyList<string> ValidateSupervisor(
        AiWorkflowRun run, SupervisorDecision decision, IReadOnlyList<string> allowed) =>
        ContentRules.ValidateSupervisor(decision, run.Id, allowed);

    public static IReadOnlyList<string> ValidateReview(AiWorkflowRun run, ReviewReport report, ScriptDraft script) =>
        ContentRules.ValidateReview(report, run.Id, script);

    /// <summary>Speaker và character trong layout chỉ được là entry Character của Bible tại Chapter đang chạy.</summary>
    public static HashSet<Guid> CharacterIds(RunSnapshot snap) =>
        snap.Bible.Where(b => b.Category == BibleEntryCategory.Character.ToString()).Select(b => b.EntryId).ToHashSet();

    public static HashSet<Guid> RevisionIds(RunSnapshot snap) => snap.Bible.Select(b => b.RevisionId).ToHashSet();

    public static string Join(IEnumerable<string> errors) => string.Join(" | ", errors.Take(10));
}