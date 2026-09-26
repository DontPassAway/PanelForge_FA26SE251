using FluentAssertions;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Enums;
using Xunit;

namespace PanelForge.UnitTests.Domain;

/// <summary>UC-03 bước 3: Producer chỉnh stage của pipeline Series.</summary>
public class PipelineDefinitionEditingTests
{
    private static PipelineDefinition CreatePipeline(params (string Name, WorkspaceRole Role, bool Gate)[] stages)
    {
        var pipeline = PipelineDefinition.Create(Guid.NewGuid(), "Test pipeline", seriesId: Guid.NewGuid());
        foreach (var (name, role, gate) in stages)
            pipeline.InsertStage(name, null, null, null, role, gate, null);
        pipeline.RebuildDefaultTransitions();
        return pipeline;
    }

    private static PipelineDefinition Standard() => CreatePipeline(
        ("Script", WorkspaceRole.Writer, false),
        ("Pencil", WorkspaceRole.Artist, false),
        ("Review", WorkspaceRole.Editor, true),
        ("Approved", WorkspaceRole.Editor, false));

    private static IEnumerable<string> Names(PipelineDefinition p) => p.GetOrderedStages().Select(s => s.Name);

    [Fact]
    public void InsertStage_AtPosition_ShouldShiftFollowingStages_AndKeepOrderContiguous()
    {
        var pipeline = Standard();

        pipeline.InsertStage("Ink", null, 3, "#10B981", WorkspaceRole.Artist, false, 2);

        Names(pipeline).Should().Equal("Script", "Pencil", "Ink", "Review", "Approved");
        pipeline.GetOrderedStages().Select(s => s.StageOrder).Should().Equal(1, 2, 3, 4, 5);
    }

    [Fact]
    public void InsertStage_WithoutPosition_ShouldAppend_AndMoveTerminalFlag()
    {
        var pipeline = Standard();

        var published = pipeline.InsertStage("Published", null, null, null, WorkspaceRole.Producer, false, null);

        published.IsTerminal.Should().BeTrue();
        pipeline.GetOrderedStages().Single(s => s.Name == "Approved").IsTerminal.Should().BeFalse();
        pipeline.GetOrderedStages().Count(s => s.IsInitial).Should().Be(1);
    }

    [Fact]
    public void InsertStage_ShouldGenerateAsciiSlugFromVietnameseName()
    {
        var pipeline = Standard();

        var stage = pipeline.InsertStage("Tô màu & Đổ bóng", null, null, null, null, false, null);

        stage.Slug.Should().Be("to-mau-do-bong");
    }

    [Fact]
    public void InsertStage_WithDuplicateSlug_ShouldThrow()
    {
        var pipeline = Standard();

        var act = () => pipeline.InsertStage("Script again", "script", null, null, null, false, null);

        act.Should().Throw<ArgumentException>().WithMessage("*script*");
    }

    [Fact]
    public void ReorderStages_ShouldApplyNewOrder_AndBoundaryFlags()
    {
        var pipeline = Standard();
        var ids = pipeline.GetOrderedStages().Select(s => s.Id).ToList();

        pipeline.ReorderStages([ids[1], ids[0], ids[2], ids[3]]);

        Names(pipeline).Should().Equal("Pencil", "Script", "Review", "Approved");
        pipeline.GetOrderedStages()[0].IsInitial.Should().BeTrue();
        pipeline.GetOrderedStages()[1].IsInitial.Should().BeFalse();
    }

    [Fact]
    public void ReorderStages_WithMissingOrUnknownIds_ShouldThrow()
    {
        var pipeline = Standard();
        var ids = pipeline.GetOrderedStages().Select(s => s.Id).ToList();

        var missing = () => pipeline.ReorderStages(ids.Take(3).ToList());
        var unknown = () => pipeline.ReorderStages([ids[0], ids[1], ids[2], Guid.NewGuid()]);
        var duplicate = () => pipeline.ReorderStages([ids[0], ids[0], ids[2], ids[3]]);

        missing.Should().Throw<ArgumentException>();
        unknown.Should().Throw<ArgumentException>();
        duplicate.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void RemoveStage_ShouldCloseGap_AndRefuseBelowTwoStages()
    {
        var pipeline = CreatePipeline(
            ("Script", WorkspaceRole.Writer, false),
            ("Pencil", WorkspaceRole.Artist, false),
            ("Approved", WorkspaceRole.Editor, false));

        pipeline.RemoveStage(pipeline.GetOrderedStages()[1].Id);

        Names(pipeline).Should().Equal("Script", "Approved");
        pipeline.GetOrderedStages().Select(s => s.StageOrder).Should().Equal(1, 2);

        var act = () => pipeline.RemoveStage(pipeline.GetOrderedStages()[0].Id);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void RebuildDefaultTransitions_ShouldLinkForward_AndAddRejectsFromApprovalGate()
    {
        var pipeline = Standard();
        var s = pipeline.GetOrderedStages();

        var forward = pipeline.Transitions.Where(t => !t.IsBackwardTransition).ToList();
        forward.Select(t => (t.FromStageId, t.ToStageId)).Should().BeEquivalentTo(new[]
        {
            (s[0].Id, s[1].Id), (s[1].Id, s[2].Id), (s[2].Id, s[3].Id)
        });
        forward.Single(t => t.FromStageId == s[0].Id).RequiredRole.Should().Be(WorkspaceRole.Writer);

        var rejects = pipeline.Transitions.Where(t => t.IsBackwardTransition).ToList();
        rejects.Should().OnlyContain(t => t.FromStageId == s[2].Id && t.RequiresComment && t.RequiredRole == WorkspaceRole.Editor);
        rejects.Select(t => t.ToStageId).Should().BeEquivalentTo(new[] { s[0].Id, s[1].Id });
    }

    [Fact]
    public void RebuildDefaultTransitions_ShouldReturnOldTransitionsAsRemoved()
    {
        var pipeline = Standard();
        var before = pipeline.Transitions.ToList();

        var (removed, added) = pipeline.RebuildDefaultTransitions();

        removed.Should().BeEquivalentTo(before);
        added.Should().HaveCount(before.Count);
        pipeline.Transitions.Should().NotContain(before);
    }
}
