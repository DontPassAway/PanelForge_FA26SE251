namespace PanelForge.Domain.Entities.Workflow;

/// <summary>
/// Tiền đề trực tiếp của một công đoạn theo luật nhánh (BR-19), chỉ xét công đoạn đang bật nên công đoạn tắt được “đi xuyên qua”.
/// <list type="bullet">
/// <item>Công đoạn trong nhánh: công đoạn đang bật liền trước cùng nhánh, hoặc công đoạn chung liền trước nếu là đầu nhánh.</item>
/// <item>Công đoạn chung: công đoạn cuối của mọi nhánh nằm sau công đoạn chung liền trước; không có nhánh thì là công đoạn chung đó.</item>
/// </list>
/// Ví dụ Pencil → (A: Ink → Color) ∥ (B: Lettering) → Review: Ink ← Pencil, Color ← Ink, Lettering ← Pencil, Review ← Color + Lettering.
/// </summary>
public static class StageDependencies
{
    public static IReadOnlyList<PipelineStage> DirectPrerequisites(PipelineStage stage, IEnumerable<PipelineStage> pipeline)
    {
        var earlier = pipeline
            .Where(s => s.IsEnabled && s.Id != stage.Id
                        && s.PipelineDefinitionId == stage.PipelineDefinitionId && s.StageOrder < stage.StageOrder)
            .OrderBy(s => s.StageOrder)
            .ToList();

        if (stage.Track is { } track)
        {
            var previous = earlier.LastOrDefault(s => s.Track == track || s.Track is null);
            return previous is null ? [] : [previous];
        }

        var lastShared = earlier.LastOrDefault(s => s.Track is null);
        var branchEnds = earlier
            .Where(s => s.Track is not null && (lastShared is null || s.StageOrder > lastShared.StageOrder))
            .GroupBy(s => s.Track)
            .Select(g => g.Last())
            .ToList();

        if (branchEnds.Count > 0) return branchEnds;
        return lastShared is null ? [] : [lastShared];
    }
}
