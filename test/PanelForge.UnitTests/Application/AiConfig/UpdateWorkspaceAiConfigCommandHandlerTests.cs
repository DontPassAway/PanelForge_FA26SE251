using FluentAssertions;
using Moq;
using PanelForge.Application.Common;
using PanelForge.Application.Features.AiConfig.Commands;
using PanelForge.Application.Interfaces;
using PanelForge.Application.Interfaces.Persistence;
using PanelForge.Domain.Entities.Auth;
using PanelForge.UnitTests.Common;
using Xunit;

namespace PanelForge.UnitTests.Application.AiConfig;

public class UpdateWorkspaceAiConfigCommandHandlerTests
{
    private readonly Mock<IPanelForgeDbContext> _dbContextMock;
    private readonly Mock<IAiKeyProtector> _keyProtectorMock;
    private readonly Mock<IAiProviderValidator> _providerValidatorMock;

    public UpdateWorkspaceAiConfigCommandHandlerTests()
    {
        _dbContextMock = new Mock<IPanelForgeDbContext>();
        _keyProtectorMock = new Mock<IAiKeyProtector>();
        _providerValidatorMock = new Mock<IAiProviderValidator>();
    }

    [Fact]
    public async Task Handle_WhenWorkspaceNotFound_ShouldReturnFailure()
    {
        // Arrange
        var workspaces = new List<StudioWorkspace>();
        _dbContextMock.Setup(db => db.StudioWorkspaces).Returns(DbSetMockHelper.CreateDbSetMock(workspaces));

        var handler = new UpdateWorkspaceAiConfigCommandHandler(
            _dbContextMock.Object, _keyProtectorMock.Object, _providerValidatorMock.Object);

        var command = new UpdateWorkspaceAiConfigCommand(
            WorkspaceId: Guid.NewGuid(),
            Provider: "Google Gemini",
            ApiKey: "AIzaSyValidGeminiKey12345",
            IsEnabled: true,
            MonthlyTokenQuota: 500000,
            AllowedModels: ["gemini-1.5-flash"]
        );

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("không tồn tại");
        _keyProtectorMock.Verify(p => p.Protect(It.IsAny<string>()), Times.Never);
        _dbContextMock.Verify(db => db.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenApiKeyInvalid_ShouldReturnFailure_AndNotSave()
    {
        // Arrange
        var workspace = StudioWorkspace.Create("Test Studio", Guid.NewGuid());
        var workspaces = new List<StudioWorkspace> { workspace };
        _dbContextMock.Setup(db => db.StudioWorkspaces).Returns(DbSetMockHelper.CreateDbSetMock(workspaces));

        _providerValidatorMock
            .Setup(v => v.ValidateCredentialsAsync("Google Gemini", "invalid-key", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool>.Failure("API Key của Google Gemini không hợp lệ"));

        var handler = new UpdateWorkspaceAiConfigCommandHandler(
            _dbContextMock.Object, _keyProtectorMock.Object, _providerValidatorMock.Object);

        var command = new UpdateWorkspaceAiConfigCommand(
            WorkspaceId: workspace.Id,
            Provider: "Google Gemini",
            ApiKey: "invalid-key",
            IsEnabled: true,
            MonthlyTokenQuota: 500000,
            AllowedModels: ["gemini-1.5-flash"]
        );

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Xác thực thông tin cấu hình AI Provider thất bại");
        _keyProtectorMock.Verify(p => p.Protect(It.IsAny<string>()), Times.Never);
        _dbContextMock.Verify(db => db.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenApiKeyValid_ShouldEncryptKey_AndSaveConfig()
    {
        // Arrange
        var workspace = StudioWorkspace.Create("Test Studio", Guid.NewGuid());
        var workspaces = new List<StudioWorkspace> { workspace };
        var configs = new List<WorkspaceAiConfig>();

        _dbContextMock.Setup(db => db.StudioWorkspaces).Returns(DbSetMockHelper.CreateDbSetMock(workspaces));
        _dbContextMock.Setup(db => db.WorkspaceAiConfigs).Returns(DbSetMockHelper.CreateDbSetMock(configs));
        _dbContextMock.Setup(db => db.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        const string rawKey = "AIzaSyValidGeminiKey12345";
        const string encryptedKey = "ENC:AIzaSyValidGeminiKey12345";

        _providerValidatorMock
            .Setup(v => v.ValidateCredentialsAsync("Google Gemini", rawKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool>.Success(true));

        _keyProtectorMock
            .Setup(p => p.Protect(rawKey))
            .Returns(encryptedKey);

        var handler = new UpdateWorkspaceAiConfigCommandHandler(
            _dbContextMock.Object, _keyProtectorMock.Object, _providerValidatorMock.Object);

        var command = new UpdateWorkspaceAiConfigCommand(
            WorkspaceId: workspace.Id,
            Provider: "Google Gemini",
            ApiKey: rawKey,
            IsEnabled: true,
            MonthlyTokenQuota: 750000,
            AllowedModels: ["gemini-1.5-pro", "gemini-1.5-flash"]
        );

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.HasApiKey.Should().BeTrue();
        result.Value.MonthlyTokenQuota.Should().Be(750000);
        result.Value.AllowedModels.Should().Contain("gemini-1.5-pro");

        _keyProtectorMock.Verify(p => p.Protect(rawKey), Times.Once);
        _dbContextMock.Verify(db => db.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        configs.Should().HaveCount(1);
        configs[0].ApiKeyEncrypted.Should().Be(encryptedKey);
    }

    [Fact]
    public async Task Handle_WhenApiKeyOmitted_OnExistingConfig_ShouldPreserveExistingKey()
    {
        // Arrange
        var workspace = StudioWorkspace.Create("Test Studio", Guid.NewGuid());
        var existingConfig = WorkspaceAiConfig.Create(
            workspace.Id, "Google Gemini", "PREVIOUS_ENCRYPTED_KEY", 1000000);

        var workspaces = new List<StudioWorkspace> { workspace };
        var configs = new List<WorkspaceAiConfig> { existingConfig };

        _dbContextMock.Setup(db => db.StudioWorkspaces).Returns(DbSetMockHelper.CreateDbSetMock(workspaces));
        _dbContextMock.Setup(db => db.WorkspaceAiConfigs).Returns(DbSetMockHelper.CreateDbSetMock(configs));
        _dbContextMock.Setup(db => db.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new UpdateWorkspaceAiConfigCommandHandler(
            _dbContextMock.Object, _keyProtectorMock.Object, _providerValidatorMock.Object);

        var command = new UpdateWorkspaceAiConfigCommand(
            WorkspaceId: workspace.Id,
            Provider: "Google Gemini",
            ApiKey: null, // Không truyền key mới -> giữ nguyên key cũ
            IsEnabled: true,
            MonthlyTokenQuota: 2000000,
            AllowedModels: ["gemini-1.5-pro"]
        );

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.HasApiKey.Should().BeTrue();
        result.Value.MonthlyTokenQuota.Should().Be(2000000);

        existingConfig.ApiKeyEncrypted.Should().Be("PREVIOUS_ENCRYPTED_KEY");
        _providerValidatorMock.Verify(
            v => v.ValidateCredentialsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _keyProtectorMock.Verify(p => p.Protect(It.IsAny<string>()), Times.Never);
    }
}
