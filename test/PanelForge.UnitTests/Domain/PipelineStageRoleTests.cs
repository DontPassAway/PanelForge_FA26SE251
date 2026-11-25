using FluentAssertions;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Enums;
using Xunit;

namespace PanelForge.UnitTests.Domain;

/// <summary>BR-31: Lettering mặc định do Artist làm, Letterer là phụ việc tuỳ chọn.</summary>
public class PipelineStageRoleTests
{
    private static PipelineStage Stage(WorkspaceRole? role) =>
        PipelineStage.Create(Guid.NewGuid(), "Stage", "stage", 1, allowedRole: role);

    [Fact]
    public void Letter_stage_accepts_both_Letterer_and_Artist()
    {
        var letter = Stage(WorkspaceRole.Letterer);

        letter.CanBeHeldBy(WorkspaceRole.Letterer).Should().BeTrue();
        letter.CanBeHeldBy(WorkspaceRole.Artist).Should().BeTrue();
        letter.CanBeHeldBy(WorkspaceRole.Writer).Should().BeFalse();
        letter.CanBeHeldBy(WorkspaceRole.Editor).Should().BeFalse();
    }

    [Fact]
    public void Artist_stage_still_rejects_Letterer()
    {
        var pencil = Stage(WorkspaceRole.Artist);

        pencil.CanBeHeldBy(WorkspaceRole.Artist).Should().BeTrue();
        pencil.CanBeHeldBy(WorkspaceRole.Letterer).Should().BeFalse();
    }

    [Fact]
    public void Stage_without_role_accepts_anyone()
    {
        Stage(null).CanBeHeldBy(WorkspaceRole.Writer).Should().BeTrue();
    }
}
