using FluentAssertions;
using Moq;
using PanelForge.Application.Features.AiConfig.Commands.UpdateWorkspaceAiConfig;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Domain.Entities.Ai;
using PanelForge.Domain.Entities.Workspaces;
using PanelForge.UnitTests.Common;
using Xunit;

namespace PanelForge.UnitTests.Application.AiConfig;

/// <summary>TASK-350: ai-config chỉ còn bật/tắt, quota, model; key nằm ở API dùng chung.</summary>
public class UpdateWorkspaceAiConfigCommandHandlerTests
{
    private readonly Mock<IPanelForgeDbContext> _db = new();

    private UpdateWorkspaceAiConfigCommandHandler Setup(
        List<StudioWorkspace> workspaces, List<WorkspaceAiConfig> configs, List<AiProviderCredential>? creds = null)
    {
        _db.Setup(d => d.StudioWorkspaces).Returns(DbSetMockHelper.CreateDbSetMock(workspaces));
        _db.Setup(d => d.WorkspaceAiConfigs).Returns(DbSetMockHelper.CreateDbSetMock(configs));
        _db.Setup(d => d.AiProviderCredentials).Returns(DbSetMockHelper.CreateDbSetMock(creds ?? []));
        _db.Setup(d => d.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        return new UpdateWorkspaceAiConfigCommandHandler(_db.Object);
    }

    [Fact]
    public async Task Handle_WhenWorkspaceNotFound_ShouldReturnFailure()
    {
        var handler = Setup([], []);

        var result = await handler.Handle(
            new UpdateWorkspaceAiConfigCommand(Guid.NewGuid(), true, 500000, ["gemini-2.5-flash"]), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("không tồn tại");
        _db.Verify(d => d.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_NewConfig_SavesQuota_AndReportsNoApiUntilOneIsChosen()
    {
        var workspace = StudioWorkspace.Create("Test Studio", Guid.NewGuid());
        var configs = new List<WorkspaceAiConfig>();
        var handler = Setup([workspace], configs);

        var result = await handler.Handle(
            new UpdateWorkspaceAiConfigCommand(workspace.Id, true, 750000, ["gemini-2.5-pro"]), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.HasApiKey.Should().BeFalse();
        result.Value.ApiKeyUsable.Should().BeFalse();
        result.Value.MonthlyTokenQuota.Should().Be(750000);
        configs.Should().ContainSingle().Which.ApiKeyEncrypted.Should().BeNull();
    }

    [Fact]
    public async Task Handle_NullTokenQuota_KeepsQuotaSetOnResourcesScreen_OPS24()
    {
        var workspace = StudioWorkspace.Create("Test Studio", Guid.NewGuid());
        var config = WorkspaceAiConfig.Create(workspace.Id, monthlyTokenQuota: 321_000);
        var handler = Setup([workspace], [config]);

        var result = await handler.Handle(
            new UpdateWorkspaceAiConfigCommand(workspace.Id, false, null, ["gemini-2.5-pro"]), CancellationToken.None);

        result.Value!.MonthlyTokenQuota.Should().Be(321_000);
        result.Value.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_ExistingConfig_KeepsChosenSharedApi_AndReportsIt()
    {
        var workspace = StudioWorkspace.Create("Test Studio", Guid.NewGuid());
        var cred = AiProviderCredential.Create("Gemini chính", AiProviderCredential.Gemini, null, "ENC", "gemini-2.5-flash", null);
        var config = WorkspaceAiConfig.Create(workspace.Id);
        config.UseProviderCredential(cred.Id);
        var handler = Setup([workspace], [config], [cred]);

        var result = await handler.Handle(
            new UpdateWorkspaceAiConfigCommand(workspace.Id, false, 2000000, []), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ProviderCredentialId.Should().Be(cred.Id);
        result.Value.HasApiKey.Should().BeTrue();
        result.Value.ApiKeyUsable.Should().BeTrue();
        result.Value.Provider.Should().Be(AiProviderCredential.Gemini);
        result.Value.IsEnabled.Should().BeFalse();
        result.Value.MonthlyTokenQuota.Should().Be(2000000);
    }
}
