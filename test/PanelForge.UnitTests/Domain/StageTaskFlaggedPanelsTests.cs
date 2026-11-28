using FluentAssertions;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Enums;
using Xunit;

namespace PanelForge.UnitTests.Domain;

/// <summary>
/// Q27/REV-09 + cờ ảnh hưởng cấp Panel: ô lưới V3 vẫn là việc cấp trang, <see cref="StageTask.FlaggedPanelIds"/> khoanh các
/// Panel cần xem/sửa lại (rỗng = cả trang).
/// </summary>
public class StageTaskFlaggedPanelsTests
{
    private static readonly Guid P1 = Guid.NewGuid();
    private static readonly Guid P2 = Guid.NewGuid();

    private static StageTask Started()
    {
        var t = StageTask.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        t.AssignTo(Guid.NewGuid());
        t.Start(prerequisitesApproved: true);
        return t;
    }

    private static StageTask Approved()
    {
        var t = Started();
        t.Submit(Guid.NewGuid(), isApprovalGate: false);
        return t;
    }

    [Fact]
    public void Reopen_WithPanels_FlagsOnlyThosePanels()
    {
        var t = Approved();

        t.Reopen("Tóc sai màu", [P1]);

        t.Status.Should().Be(StageTaskStatus.ReworkRequired);
        t.FlaggedPanelIds.Should().Equal(P1);
    }

    [Fact]
    public void Reopen_WithoutPanels_MeansWholePage()
    {
        var t = Approved();

        t.Reopen("Sai bố cục");

        t.FlaggedPanelIds.Should().BeEmpty();
    }

    [Fact]
    public void ExtendRework_AddsPanels_AndWholePageWins()
    {
        var t = Approved();
        t.Reopen("Lỗi 1", [P1]);

        t.ExtendRework([P2]);
        t.FlaggedPanelIds.Should().BeEquivalentTo([P1, P2]);

        t.ExtendRework(null); // lỗi không gắn Panel → cả trang
        t.FlaggedPanelIds.Should().BeEmpty();
    }

    [Fact]
    public void ExtendRework_OnCellNotInRework_Throws()
    {
        FluentActions.Invoking(() => Approved().ExtendRework([P1])).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void MarkInputOutdated_RepeatedScriptChanges_UnionPanels()
    {
        var t = Started();

        t.MarkInputOutdated(StageTask.ScriptChangedReason, [P1]);
        t.MarkInputOutdated(StageTask.ScriptChangedReason, [P2]);

        t.FlaggedPanelIds.Should().BeEquivalentTo([P1, P2]);
    }

    [Fact]
    public void MarkInputOutdated_OnPanelScopedRework_WholePageChangeWidensToWholePage()
    {
        var t = Approved();
        t.Reopen("Lỗi Panel 1", [P1]);

        t.MarkInputOutdated("Pencil được mở lại");

        t.FlaggedPanelIds.Should().BeEmpty();
    }

    [Fact]
    public void ResolveImpact_KeepClears_ReworkKeepsPanels()
    {
        var keep = Started();
        keep.MarkInputOutdated(StageTask.ScriptChangedReason, [P1]);
        keep.ResolveImpact(keep: true, prerequisitesApproved: true);
        keep.FlaggedPanelIds.Should().BeEmpty();

        var rework = Started();
        rework.MarkInputOutdated(StageTask.ScriptChangedReason, [P1]);
        rework.ResolveImpact(keep: false, prerequisitesApproved: true);
        rework.Status.Should().Be(StageTaskStatus.ReworkRequired);
        rework.FlaggedPanelIds.Should().Equal(P1);
    }

    [Fact]
    public void Submit_ClearsFlaggedPanels()
    {
        var t = Approved();
        t.Reopen("Lỗi", [P1]);
        t.Start(prerequisitesApproved: true);

        t.Submit(Guid.NewGuid(), isApprovalGate: true);

        t.FlaggedPanelIds.Should().BeEmpty();
    }
}
