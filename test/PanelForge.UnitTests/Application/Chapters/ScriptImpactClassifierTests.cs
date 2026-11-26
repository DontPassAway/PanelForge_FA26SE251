using FluentAssertions;
using PanelForge.Application.Features.Chapters.Services;
using PanelForge.Application.Features.ScriptVersions;
using PanelForge.Domain.Entities.Workflow;

namespace PanelForge.UnitTests.Application.Chapters;

public sealed class ScriptImpactClassifierTests
{
    private readonly ScriptImpactClassifier _sut = new();

    [Theory]
    [InlineData("Dialogue")]
    [InlineData("Thought")]
    [InlineData("Narration")]
    [InlineData("Sfx")]
    public void TextOrSpeakerEdit_StalesBoundLine_WithoutVisualImpact(string kind)
    {
        var lineId = Guid.NewGuid();
        var panelId = Guid.NewGuid();
        var oldSnapshot = Snapshot(Panel(panelId), Line(lineId, kind, "Cũ", "An"));
        var newSnapshot = Snapshot(Panel(panelId), Line(lineId, kind, "Mới", "Bình"));

        var result = _sut.Classify(oldSnapshot, newSnapshot);

        result.StaleScriptLineIds.Should().ContainSingle().Which.Should().Be(lineId);
        result.VisualPanels.Should().BeEmpty();
    }

    [Fact]
    public void ActionAndPanelVariantEdit_HasVisualImpact_WithoutTextImpact()
    {
        var panelId = Guid.NewGuid();
        var entryId = Guid.NewGuid();
        var oldSnapshot = Snapshot(
            Panel(panelId, casts: [new ScriptVersionRowCast(entryId, Guid.NewGuid())]),
            Line(Guid.NewGuid(), "Action", "Trời mưa."));
        var newSnapshot = Snapshot(
            Panel(panelId, casts: [new ScriptVersionRowCast(entryId, Guid.NewGuid())]),
            Line(Guid.NewGuid(), "Action", "Sấm chớp dữ dội."));

        var result = _sut.Classify(oldSnapshot, newSnapshot);

        result.VisualPanels.Should().Contain(new ScriptPanelReference(1, 1, panelId));
        result.StaleScriptLineIds.Should().BeEmpty();
    }

    [Fact]
    public void ActionEdit_DoesNotFlagNotStartedStageTask()
    {
        var lineId = Guid.NewGuid();
        var impact = _sut.Classify(
            Snapshot(Panel(), Line(lineId, "Action", "Cửa đóng.")),
            Snapshot(Panel(), Line(lineId, "Action", "Cửa bật tung.")));
        var task = StageTask.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        task.AssignTo(Guid.NewGuid());

        if (impact.HasVisualImpact) task.MarkInputOutdated(StageTask.ScriptChangedReason);

        task.IsInputOutdated.Should().BeFalse();
    }

    [Fact]
    public void SimultaneousActionAndDialogueEdit_ProducesBothSignals()
    {
        var actionId = Guid.NewGuid();
        var dialogueId = Guid.NewGuid();
        var panelId = Guid.NewGuid();
        var oldSnapshot = Snapshot(
            Panel(panelId),
            Line(actionId, "Action", "Cô đứng dậy."),
            Line(dialogueId, "Dialogue", "Đi thôi.", "An"));
        var newSnapshot = Snapshot(
            Panel(panelId),
            Line(actionId, "Action", "Cô chạy ra cửa."),
            Line(dialogueId, "Dialogue", "Chạy thôi!", "An"));

        var result = _sut.Classify(oldSnapshot, newSnapshot);

        result.HasVisualImpact.Should().BeTrue();
        result.StaleScriptLineIds.Should().Contain(dialogueId);
    }

    private static ScriptVersionSnapshot Snapshot(params ScriptVersionRow[] rows) => new(rows);

    private static ScriptVersionRow Panel(
        Guid? id = null,
        string boundingBox = "{\"x\":0,\"y\":0,\"width\":1,\"height\":1}",
        IReadOnlyList<ScriptVersionRowCast>? casts = null) =>
        new("panel:1.1", "panel", 1, 1, null, string.Empty, CastRevisions: casts,
            EntityId: id ?? Guid.NewGuid(), BoundingBox: boundingBox);

    private static ScriptVersionRow Line(Guid id, string kind, string text, string? speaker = null) =>
        new($"line:{id}", kind, 1, 1, speaker, text, EntityId: id);
}
