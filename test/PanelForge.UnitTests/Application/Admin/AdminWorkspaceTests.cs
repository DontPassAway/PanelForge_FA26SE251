using FluentAssertions;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Admin.Commands.UpdateWorkspaceQuota;
using PanelForge.Application.Features.Admin.Queries.GetAdminWorkspaces;
using PanelForge.Domain.Entities.Identity;
using PanelForge.Domain.Entities.Workspaces;
using PanelForge.UnitTests.Common;
using Xunit;

namespace PanelForge.UnitTests.Application.Admin;

public class AdminWorkspaceTests
{
    [Fact]
    public async Task GetAdminWorkspaces_ShouldReturnAllWorkspaces()
    {
        await using var db = InMemoryDb.Create();
        var owner = User.Create("owner@test.com", "Studio Owner");
        db.Users.Add(owner);

        var ws1 = StudioWorkspace.Create("Studio Alpha", owner.Id, 10737418240L); // 10 GB
        var ws2 = StudioWorkspace.Create("Studio Beta", owner.Id, 5368709120L); // 5 GB
        db.StudioWorkspaces.AddRange(ws1, ws2);
        await db.SaveChangesAsync();

        var handler = new GetAdminWorkspacesQueryHandler(db);
        var result = await handler.Handle(new GetAdminWorkspacesQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
        result.Value.Select(w => w.Name).Should().Contain(["Studio Alpha", "Studio Beta"]);
    }

    [Fact]
    public async Task UpdateWorkspaceQuota_ValidQuota_ShouldUpdateSuccessfully()
    {
        await using var db = InMemoryDb.Create();
        var owner = User.Create("owner@test.com", "Studio Owner");
        db.Users.Add(owner);

        var ws = StudioWorkspace.Create("Studio To Update", owner.Id, 5368709120L);
        db.StudioWorkspaces.Add(ws);
        await db.SaveChangesAsync();

        var handler = new UpdateWorkspaceQuotaCommandHandler(db);
        long newQuota = 21474836480L; // 20 GB
        var result = await handler.Handle(new UpdateWorkspaceQuotaCommand(ws.Id, newQuota), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.StorageQuotaBytes.Should().Be(newQuota);

        var updatedWs = await db.StudioWorkspaces.FindAsync(ws.Id);
        updatedWs!.StorageQuotaBytes.Should().Be(newQuota);
    }

    [Fact]
    public async Task UpdateWorkspaceQuota_NegativeQuota_ShouldReturnFailure()
    {
        await using var db = InMemoryDb.Create();
        var handler = new UpdateWorkspaceQuotaCommandHandler(db);

        var result = await handler.Handle(new UpdateWorkspaceQuotaCommand(Guid.NewGuid(), -100L), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("không được là số âm");
    }

    [Fact]
    public async Task UpdateWorkspaceQuota_NotFound_ShouldReturnNotFound()
    {
        await using var db = InMemoryDb.Create();
        var handler = new UpdateWorkspaceQuotaCommandHandler(db);

        var result = await handler.Handle(new UpdateWorkspaceQuotaCommand(Guid.NewGuid(), 1000L), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ResultErrorCodes.NotFound);
    }
}
