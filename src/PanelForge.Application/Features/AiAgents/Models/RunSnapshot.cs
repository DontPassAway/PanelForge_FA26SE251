using PanelForge.AI.Contracts;

namespace PanelForge.Application.Features.AiAgents.Models;

/// <summary>
/// Context chụp một lần ở đầu run (Bible hiệu lực tại Chapter, script nền, và phần riêng của từng loại workflow:
/// layout đã duyệt, context Artist/Letterer, snapshot tiến độ). Lưu trong AiWorkflowRun.ContextSnapshotJson, bất biến.
/// Các trường sau BaselineScript là tuỳ chọn: snapshot của run cũ (không có chúng) vẫn đọc được.
/// </summary>
public sealed record RunSnapshot(
    decimal ChapterNumber,
    string SceneHeading,
    string? SceneSummary,
    IReadOnlyList<BibleEntryContext> Bible,
    IReadOnlyList<BaselineScriptLine> BaselineScript,
    StoryboardLayout? ExistingLayout = null,
    Guid? LayoutRevisionId = null,
    ArtistContext? Artist = null,
    LetteringContext? Lettering = null,
    ProgressContext? Progress = null)
{
    public WorkflowInputs Inputs => new(
        BaselineScript.Count > 0, ExistingLayout is not null, Artist?.Panels.Count > 0, Lettering?.Lines.Count > 0, Progress is not null);
}
