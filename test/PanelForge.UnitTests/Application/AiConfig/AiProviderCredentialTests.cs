using FluentAssertions;
using Moq;
using PanelForge.AI.Contracts;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.AiConfig.Commands.CreateAiProviderCredential;
using PanelForge.Application.Features.AiConfig.Commands.DeleteAiProviderCredential;
using PanelForge.Application.Features.AiConfig.Common;
using PanelForge.Application.Features.AiConfig.Models;
using PanelForge.Domain.Entities.Ai;
using PanelForge.Domain.Entities.Workspaces;
using PanelForge.UnitTests.Common;
using Xunit;

namespace PanelForge.UnitTests.Application.AiConfig;

/// <summary>UC-02 mở rộng: nhiều API AI. Workspace gắn credential thì dùng provider/model/key của credential.</summary>
public class AiProviderCredentialTests
{
    private readonly Mock<IPanelForgeDbContext> _db = new();
    private readonly Mock<IAiKeyProtector> _protector = new();
    private readonly Mock<IAiProviderValidator> _validator = new();

    public AiProviderCredentialTests()
    {
        _protector.Setup(p => p.Protect(It.IsAny<string>())).Returns<string>(k => "enc:" + k);
        _protector.Setup(p => p.Unprotect(It.IsAny<string>())).Returns<string>(k => k.StartsWith("enc:") ? k[4..] : "");
    }

    private static AiProviderCredential Cloudflare(bool enabled = true)
    {
        var c = AiProviderCredential.Create("CF", AiProviderCredential.Cloudflare, "acc-1", "enc:cf-token", "@cf/meta/llama", "@cf/flux");
        if (!enabled) c.Update("CF", "acc-1", null, "@cf/meta/llama", "@cf/flux", isEnabled: false);
        return c;
    }

    [Fact]
    public async Task Resolve_WithoutCredential_UsesLegacyGeminiKey()
    {
        var config = WorkspaceAiConfig.Create(Guid.NewGuid(), apiKey: "enc:gemini-key");

        var r = await AiCredentials.ResolveAsync(_db.Object, _protector.Object, config, "gemini-x", default);

        r.Should().Be(new ResolvedAiCredential(AiProtocol.ProviderGemini, "gemini-x", null, "gemini-key", null, false));
    }

    [Fact]
    public async Task Resolve_WithCloudflareCredential_UsesItsModelKeyAndAccount()
    {
        var cred = Cloudflare();
        _db.Setup(d => d.AiProviderCredentials).Returns(DbSetMockHelper.CreateDbSetMock(new List<AiProviderCredential> { cred }));
        var config = WorkspaceAiConfig.Create(Guid.NewGuid(), apiKey: "enc:gemini-key");
        config.UseProviderCredential(cred.Id);

        var r = await AiCredentials.ResolveAsync(_db.Object, _protector.Object, config, "gemini-x", default);

        r.Should().Be(new ResolvedAiCredential("Cloudflare", "@cf/meta/llama", "@cf/flux", "cf-token", "acc-1", true));
    }

    [Fact]
    public async Task Resolve_WithDisabledCredential_HasNoKey_AndDoesNotFallBackToLegacyKey()
    {
        var cred = Cloudflare(enabled: false);
        _db.Setup(d => d.AiProviderCredentials).Returns(DbSetMockHelper.CreateDbSetMock(new List<AiProviderCredential> { cred }));
        var config = WorkspaceAiConfig.Create(Guid.NewGuid(), apiKey: "enc:gemini-key");
        config.UseProviderCredential(cred.Id);

        var r = await AiCredentials.ResolveAsync(_db.Object, _protector.Object, config, "gemini-x", default);

        r.ApiKey.Should().BeNull();
        r.FromCredential.Should().BeTrue();
    }

    [Fact]
    public async Task Create_Cloudflare_WithoutAccountId_IsRejected_WithoutCallingProvider()
    {
        var handler = new CreateAiProviderCredentialCommandHandler(_db.Object, _protector.Object, _validator.Object);

        var result = await handler.Handle(new CreateAiProviderCredentialCommand(
            new CreateAiProviderCredentialRequest("CF", "Cloudflare", null, "token", "@cf/meta/llama", null)), default);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ResultErrorCodes.Validation);
        _validator.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Create_ValidCloudflare_StoresEncryptedKey_AndReturnsOnlyHint()
    {
        var store = new List<AiProviderCredential>();
        var set = DbSetMockHelper.CreateDbSetMock(store);
        _db.Setup(d => d.AiProviderCredentials).Returns(set);
        _validator.Setup(v => v.ValidateCloudflareAsync("acc-1", "secret-token-9876", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool>.Success(true));
        var handler = new CreateAiProviderCredentialCommandHandler(_db.Object, _protector.Object, _validator.Object);

        var result = await handler.Handle(new CreateAiProviderCredentialCommand(
            new CreateAiProviderCredentialRequest("CF", "Cloudflare", " acc-1 ", "secret-token-9876", "@cf/meta/llama", null)), default);

        result.IsSuccess.Should().BeTrue();
        result.Value!.KeyHint.Should().Be("••••9876");
        result.Value.AccountId.Should().Be("acc-1");
        _db.Verify(d => d.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Create_WhenProviderRejectsKey_DoesNotSave()
    {
        _validator.Setup(v => v.ValidateCredentialsAsync("Gemini", "bad", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool>.Failure("Google từ chối API key này."));
        var handler = new CreateAiProviderCredentialCommandHandler(_db.Object, _protector.Object, _validator.Object);

        var result = await handler.Handle(new CreateAiProviderCredentialCommand(
            new CreateAiProviderCredentialRequest("G", "Gemini", null, "bad", "gemini-x", null)), default);

        result.IsSuccess.Should().BeFalse();
        _db.Verify(d => d.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Delete_WhenWorkspaceStillUsesCredential_ReturnsConflict()
    {
        var cred = Cloudflare();
        var config = WorkspaceAiConfig.Create(Guid.NewGuid());
        config.UseProviderCredential(cred.Id);
        _db.Setup(d => d.AiProviderCredentials).Returns(DbSetMockHelper.CreateDbSetMock(new List<AiProviderCredential> { cred }));
        _db.Setup(d => d.WorkspaceAiConfigs).Returns(DbSetMockHelper.CreateDbSetMock(new List<WorkspaceAiConfig> { config }));

        var result = await new DeleteAiProviderCredentialCommandHandler(_db.Object)
            .Handle(new DeleteAiProviderCredentialCommand(cred.Id), default);

        result.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        _db.Verify(d => d.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
