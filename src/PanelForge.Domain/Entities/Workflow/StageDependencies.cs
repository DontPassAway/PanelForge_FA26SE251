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

    /// <summary>
    /// Gate 2 ghép (Module 3 mục 1, BR-19): nếu <paramref name="stage"/> là bước cuối đang bật của nhánh mình, trả bước cuối đang bật
    /// của các nhánh song song khác trong cùng đoạn rẽ nhánh (giữa hai công đoạn chung). Vd Ink (hoặc Color nếu bật) ↔ Lettering.
    /// Không phải bước cuối nhánh, hoặc không có nhánh song song, thì rỗng.
    /// </summary>
    public static IReadOnlyList<PipelineStage> SiblingBranchEnds(PipelineStage stage, IEnumerable<PipelineStage> pipeline)
    {
        if (!stage.IsEnabled || stage.Track is not { } track) return [];
        var enabled = pipeline
            .Where(s => s.IsEnabled && s.PipelineDefinitionId == stage.PipelineDefinitionId)
            .OrderBy(s => s.StageOrder)
            .ToList();
        var from = enabled.LastOrDefault(s => s.Track is null && s.StageOrder < stage.StageOrder)?.StageOrder ?? int.MinValue;
        var to = enabled.FirstOrDefault(s => s.Track is null && s.StageOrder > stage.StageOrder)?.StageOrder ?? int.MaxValue;
        var group = enabled.Where(s => s.Track is not null && s.StageOrder > from && s.StageOrder < to).ToList();

        if (group.Last(s => s.Track == track).Id != stage.Id) return [];
        return group.Where(s => s.Track != track).GroupBy(s => s.Track).Select(g => g.Last()).ToList();
    }
}
