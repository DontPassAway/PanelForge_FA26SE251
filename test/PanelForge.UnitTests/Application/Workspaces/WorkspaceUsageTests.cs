using FluentAssertions;
using Moq;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Workspaces.Queries.GetWorkspaceUsage;
using PanelForge.Domain.Entities.Workspaces;
using PanelForge.UnitTests.Common;
using Xunit;

namespace PanelForge.UnitTests.Application.Workspaces;

/// <summary>V1.2 (CF1): Producer xem mức dùng Workspace; storage vượt quota bị chặn ở một điểm duy nhất (TryReserveStorage).</summary>
public class WorkspaceUsageTests
{
    private static GetWorkspaceUsageQueryHandler Handler(
        StudioWorkspace? workspace, WorkspaceAiConfig? ai, Guid workspaceId)
    {
        if (workspace is not null) typeof(StudioWorkspace).GetProperty("Id")!.SetValue(workspace, workspaceId);
        var db = new Mock<IPanelForgeDbContext>();
        db.Setup(d => d.StudioWorkspaces).Returns(DbSetMockHelper.CreateDbSetMock(
            workspace is null ? new List<StudioWorkspace>() : new List<StudioWorkspace> { workspace }));
        db.Setup(d => d.WorkspaceAiConfigs).Returns(DbSetMockHelper.CreateDbSetMock(
            ai is null ? new List<WorkspaceAiConfig>() : new List<WorkspaceAiConfig> { ai }));
        return new GetWorkspaceUsageQueryHandler(db.Object);
    }

    [Fact]
    public async Task Usage_ReportsStorageAndAiTokensAgainstLimits()
    {
        var id = Guid.NewGuid();
        var workspace = StudioWorkspace.Create("Studio", Guid.NewGuid(), 1000);
        workspace.TryReserveStorage(250).Should().BeTrue();
        var ai = WorkspaceAiConfig.Create(id, monthlyTokenQuota: 2000);
        ai.RecordUsage(500);

        var result = await Handler(workspace, ai, id).Handle(new GetWorkspaceUsageQuery(id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Storage.Should().Be(new UsageMeterDto(250, 1000, false, 25.0));
        result.Value.AiTokens.Should().Be(new UsageMeterDto(500, 2000, false, 25.0));
        result.Value.AiEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task Usage_ZeroQuota_IsUnlimitedWithoutPercent_AndMissingAiConfigIsNull()
    {
        var id = Guid.NewGuid();
        var workspace = StudioWorkspace.Create("Studio", Guid.NewGuid(), 0);
        workspace.TryReserveStorage(999).Should().BeTrue();

        var result = await Handler(workspace, null, id).Handle(new GetWorkspaceUsageQuery(id), CancellationToken.None);

        result.Value!.Storage.Should().Be(new UsageMeterDto(999, 0, true, null));
        result.Value.AiTokens.Should().BeNull();
        result.Value.AiEnabled.Should().BeNull();
    }

    [Fact]
    public async Task Usage_UnknownWorkspace_ReturnsNotFound()
    {
        var result = await Handler(null, null, Guid.NewGuid()).Handle(new GetWorkspaceUsageQuery(Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ResultErrorCodes.NotFound);
    }

    [Fact]
    public void TryReserveStorage_WithinQuota_AddsBytes_ExceedingQuota_ChangesNothing()
    {
        var workspace = StudioWorkspace.Create("Studio", Guid.NewGuid(), 1000);

        workspace.TryReserveStorage(600).Should().BeTrue();
        workspace.TryReserveStorage(401).Should().BeFalse();
        workspace.UsedStorageBytes.Should().Be(600);
        workspace.TryReserveStorage(400).Should().BeTrue();
        workspace.UsedStorageBytes.Should().Be(1000);
    }

    [Fact]
    public void ReleaseStorage_FreesBytes_ButNeverGoesBelowZero()
    {
        var workspace = StudioWorkspace.Create("Studio", Guid.NewGuid(), 1000);
        workspace.TryReserveStorage(300);

        workspace.ReleaseStorage(100);
        workspace.UsedStorageBytes.Should().Be(200);

        workspace.ReleaseStorage(5000);
        workspace.UsedStorageBytes.Should().Be(0);
    }
}
