namespace PanelForge.Domain.Models;

/// <summary>
/// Structured Dialogue AST Node inside a Comic Panel.
/// </summary>
public record PanelDialogueItem(string Character, string? Modifier, string Text);

/// <summary>
/// Structured Sound Effect (SFX) AST Node inside a Comic Panel.
/// </summary>
public record PanelSfxItem(string Text);

/// <summary>
/// Analysis result for Intra-Page Scene Transitions.
/// Validates that a single page can transition between scenes (e.g. Present -> Flashback)
/// via independent panel-to-scene bindings (panel.sceneId).
/// </summary>
public class IntraPageSceneAnalysis
{
    public bool IsMultiScene { get; init; }
    public IReadOnlyList<Guid> SceneIds { get; init; } = [];
    public IReadOnlyList<SceneTransitionStep> Transitions { get; init; } = [];

    public record SceneTransitionStep(Guid FromSceneId, Guid ToSceneId, int AtPanelNumber);

    public static IntraPageSceneAnalysis Analyze(IEnumerable<(int PanelNumber, Guid SceneId, bool IsFlashback)> panels)
    {
        var sorted = panels.OrderBy(p => p.PanelNumber).ToList();
        var sceneIds = sorted.Select(p => p.SceneId).Distinct().ToList();
        var transitions = new List<SceneTransitionStep>();

        for (int i = 1; i < sorted.Count; i++)
        {
            if (sorted[i].SceneId != sorted[i - 1].SceneId)
            {
                transitions.Add(new SceneTransitionStep(
                    sorted[i - 1].SceneId,
                    sorted[i].SceneId,
                    sorted[i].PanelNumber));
            }
        }

        return new IntraPageSceneAnalysis
        {
            IsMultiScene = sceneIds.Count > 1,
            SceneIds = sceneIds,
            Transitions = transitions
        };
    }
}
