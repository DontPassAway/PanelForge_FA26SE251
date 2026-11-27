using FluentAssertions;
using Moq;
using PanelForge.AI.Contracts;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.AiConfig.Commands.CreateAiProviderCredential;
using PanelForge.Application.Features.AiConfig.Commands.DeleteAiProviderCredential;
using PanelForge.Application.Features.AiConfig.Commands.SetWorkspaceAiImageProvider;
using PanelForge.Application.Features.AiConfig.Commands.SetDefaultAiProvider;
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
        _db.Setup(d => d.AiProviderCredentials).Returns(DbSetMockHelper.CreateDbSetMock(new List<AiProviderCredential>()));
    }

    private static AiProviderCredential Cloudflare(bool enabled = true)
    {
        var c = AiProviderCredential.Create("CF", AiProviderCredential.Cloudflare, "acc-1", "enc:cf-token", "@cf/meta/llama", "@cf/flux");
        if (!enabled) c.Update("CF", "acc-1", null, "@cf/meta/llama", "@cf/flux", isEnabled: false);
        return c;
    }

    [Fact]
    public async Task Resolve_WithoutCredential_HasNoKey_EvenIfLegacyStudioKeyExists()
    {
        var config = WorkspaceAiConfig.Create(Guid.NewGuid(), apiKey: "enc:gemini-key");

        var r = await AiCredentials.ResolveAsync(_db.Object, _protector.Object, config, "gemini-x", default);

        r.ApiKey.Should().BeNull("TASK-350: không còn rơi về key Gemini riêng của Studio");
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
    public async Task ResolveImage_UsesSeparateImageCredential_WhileTextWithoutCredentialHasNoKey()
    {
        var cred = Cloudflare();
        _db.Setup(d => d.AiProviderCredentials).Returns(DbSetMockHelper.CreateDbSetMock(new List<AiProviderCredential> { cred }));
        var config = WorkspaceAiConfig.Create(Guid.NewGuid(), apiKey: "enc:gemini-key");
        config.UseImageProviderCredential(cred.Id);

        var text = await AiCredentials.ResolveAsync(_db.Object, _protector.Object, config, "gemini-x", default);
        var image = await AiCredentials.ResolveImageAsync(_db.Object, _protector.Object, config, "gemini-x", default);

        text.ApiKey.Should().BeNull();
        image.Should().Be(new ResolvedAiCredential("Cloudflare", "@cf/meta/llama", "@cf/flux", "cf-token", "acc-1", true));
    }

    [Fact]
    public async Task ResolveImage_WithDisabledImageCredential_HasNoModelOrKey()
    {
        var cred = Cloudflare(enabled: false);
        _db.Setup(d => d.AiProviderCredentials).Returns(DbSetMockHelper.CreateDbSetMock(new List<AiProviderCredential> { cred }));
        var config = WorkspaceAiConfig.Create(Guid.NewGuid(), apiKey: "enc:gemini-key");
        config.UseImageProviderCredential(cred.Id);

        var image = await AiCredentials.ResolveImageAsync(_db.Object, _protector.Object, config, "gemini-x", default);

        image.ImageModel.Should().BeNull();
        image.ApiKey.Should().BeNull();
    }

    [Fact]
    public async Task SetImageProvider_RejectsCredentialWithoutImageModel()
    {
        var textOnly = AiProviderCredential.Create("CF chữ", AiProviderCredential.Cloudflare, "acc-1", "enc:t", "@cf/meta/llama", null);
        var workspace = StudioWorkspace.Create("Studio", Guid.NewGuid());
        _db.Setup(d => d.StudioWorkspaces).Returns(DbSetMockHelper.CreateDbSetMock(new List<StudioWorkspace> { workspace }));
        _db.Setup(d => d.AiProviderCredentials).Returns(DbSetMockHelper.CreateDbSetMock(new List<AiProviderCredential> { textOnly }));

        var result = await new SetWorkspaceAiImageProviderCommandHandler(_db.Object)
            .Handle(new SetWorkspaceAiImageProviderCommand(workspace.Id, textOnly.Id), default);

        result.ErrorCode.Should().Be(ResultErrorCodes.Validation);
        _db.Verify(d => d.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
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

    // ── TASK-355: API mặc định ────────────────────────────────────────────────
    private static AiProviderCredential Gemini(string key = "enc:g-key")
        => AiProviderCredential.Create("G", AiProviderCredential.Gemini, null, key, "gemini-2.5-flash", null);

    [Fact]
    public async Task Resolve_StudioWithoutChoice_UsesDefaultText_AndDefaultImage()
    {
        var text = Gemini(); text.SetDefaultText(true);
        var img = Cloudflare(); img.SetDefaultImage(true);
        _db.Setup(d => d.AiProviderCredentials).Returns(DbSetMockHelper.CreateDbSetMock(new List<AiProviderCredential> { text, img }));
        var config = WorkspaceAiConfig.Create(Guid.NewGuid());

        var t = await AiCredentials.ResolveAsync(_db.Object, _protector.Object, config, "gemini-x", default);
        var i = await AiCredentials.ResolveImageAsync(_db.Object, _protector.Object, config, "gemini-x", default);

        t.ApiKey.Should().Be("g-key");
        t.TextModel.Should().Be("gemini-2.5-flash");
        i.ApiKey.Should().Be("cf-token");
        i.ImageModel.Should().Be("@cf/flux");
    }

    [Fact]
    public async Task Resolve_StudioChoice_BeatsDefault_AndDisabledDefaultIsIgnored()
    {
        var def = Gemini("enc:default"); def.SetDefaultText(true);
        var own = Gemini("enc:own");
        var off = Gemini("enc:off"); off.SetDefaultText(true); off.Update("G", null, null, "gemini-2.5-flash", null, isEnabled: false);
        var config = WorkspaceAiConfig.Create(Guid.NewGuid());
        config.UseProviderCredential(own.Id);

        _db.Setup(d => d.AiProviderCredentials).Returns(DbSetMockHelper.CreateDbSetMock(new List<AiProviderCredential> { def, own }));
        (await AiCredentials.ResolveAsync(_db.Object, _protector.Object, config, "x", default)).ApiKey.Should().Be("own");

        _db.Setup(d => d.AiProviderCredentials).Returns(DbSetMockHelper.CreateDbSetMock(new List<AiProviderCredential> { off }));
        (await AiCredentials.ResolveAsync(_db.Object, _protector.Object, WorkspaceAiConfig.Create(Guid.NewGuid()), "x", default))
            .ApiKey.Should().BeNull("API mặc định đang tắt thì không dùng");
    }

    [Fact]
    public async Task SetDefault_MovesTheFlag_AndValidatesKindAndImageModel()
    {
        var a = Gemini(); a.SetDefaultText(true);
        var b = Gemini();
        var cf = Cloudflare();
        var textOnly = Gemini();
        _db.Setup(d => d.AiProviderCredentials).Returns(DbSetMockHelper.CreateDbSetMock(new List<AiProviderCredential> { a, b, cf, textOnly }));
        _db.Setup(d => d.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        var handler = new SetDefaultAiProviderCommandHandler(_db.Object);

        (await handler.Handle(new("text", b.Id), default)).IsSuccess.Should().BeTrue();
        a.IsDefaultText.Should().BeFalse();
        b.IsDefaultText.Should().BeTrue();

        (await handler.Handle(new("image", cf.Id), default)).IsSuccess.Should().BeTrue();
        cf.IsDefaultImage.Should().BeTrue();
        (await handler.Handle(new("image", textOnly.Id), default)).ErrorCode.Should().Be(ResultErrorCodes.Validation);
        (await handler.Handle(new("video", b.Id), default)).ErrorCode.Should().Be(ResultErrorCodes.Validation);

        (await handler.Handle(new("text", null), default)).IsSuccess.Should().BeTrue();
        b.IsDefaultText.Should().BeFalse("null = bỏ API mặc định");
    }

    [Fact]
    public async Task Delete_DefaultCredential_ReturnsConflict_EvenWhenZeroWorkspacesExplicitlyAssigned()
    {
        var def = Gemini(); def.SetDefaultText(true);
        _db.Setup(d => d.AiProviderCredentials).Returns(DbSetMockHelper.CreateDbSetMock(new List<AiProviderCredential> { def }));
        _db.Setup(d => d.WorkspaceAiConfigs).Returns(DbSetMockHelper.CreateDbSetMock(new List<WorkspaceAiConfig>()));

        var result = await new DeleteAiProviderCredentialCommandHandler(_db.Object)
            .Handle(new DeleteAiProviderCredentialCommand(def.Id), default);

        result.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        _db.Verify(d => d.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Update_EmptyKeyKeepsOldKey_RejectsMaskedKey_AndBlocksDisablingOrClearingImageModelOnDefault()
    {
        var cred = Cloudflare();
        cred.SetDefaultImage(true);
        _db.Setup(d => d.AiProviderCredentials).Returns(DbSetMockHelper.CreateDbSetMock(new List<AiProviderCredential> { cred }));
        _db.Setup(d => d.WorkspaceAiConfigs).Returns(DbSetMockHelper.CreateDbSetMock(new List<WorkspaceAiConfig>()));
        var handler = new PanelForge.Application.Features.AiConfig.Commands.UpdateAiProviderCredential.UpdateAiProviderCredentialCommandHandler(
            _db.Object, _protector.Object, _validator.Object);

        // 1) Để trống ApiKey → giữ nguyên key cũ, không gọi validator
        var keep = await handler.Handle(new(cred.Id, new UpdateAiProviderCredentialRequest(
            "CF đổi tên", "acc-1", "   ", "@cf/meta/llama-new", "@cf/flux", true)), default);
        keep.IsSuccess.Should().BeTrue();
        cred.ApiKeyEncrypted.Should().Be("enc:cf-token");
        _validator.VerifyNoOtherCalls();

        // 2) Dán nhầm chuỗi mask "••••" → chặn Validation
        var masked = await handler.Handle(new(cred.Id, new UpdateAiProviderCredentialRequest(
            "CF", "acc-1", "••••token", "@cf/meta/llama-new", "@cf/flux", true)), default);
        masked.ErrorCode.Should().Be(ResultErrorCodes.Validation);

        // 3) Tắt API đang là mặc định → chặn Validation
        var disableDefault = await handler.Handle(new(cred.Id, new UpdateAiProviderCredentialRequest(
            "CF", "acc-1", null, "@cf/meta/llama-new", "@cf/flux", false)), default);
        disableDefault.ErrorCode.Should().Be(ResultErrorCodes.Validation);

        // 4) Xoá trống ImageModel trên API đang là mặc định sinh ảnh → chặn Validation
        var clearImage = await handler.Handle(new(cred.Id, new UpdateAiProviderCredentialRequest(
            "CF", "acc-1", null, "@cf/meta/llama-new", null, true)), default);
        clearImage.ErrorCode.Should().Be(ResultErrorCodes.Validation);
    }

    [Fact]
    public async Task Test_UsesStrictProbe_AndReportsUndecryptableKey()
    {
        var ok = Gemini("enc:good");
        var broken = Gemini("corrupted-ciphertext");
        _db.Setup(d => d.AiProviderCredentials).Returns(DbSetMockHelper.CreateDbSetMock(new List<AiProviderCredential> { ok, broken }));
        _validator.Setup(v => v.ProbeAsync("Gemini", null, "good", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool>.Failure("API key hợp lệ nhưng đang bị giới hạn tần suất (HTTP 429)."));
        var handler = new PanelForge.Application.Features.AiConfig.Commands.TestAiProviderCredential.TestAiProviderCredentialCommandHandler(
            _db.Object, _protector.Object, _validator.Object);

        var brokenRes = await handler.Handle(new(broken.Id), default);
        brokenRes.IsSuccess.Should().BeFalse();
        brokenRes.ErrorMessage.Should().Contain("không giải mã được");

        var rateLimited = await handler.Handle(new(ok.Id), default);
        rateLimited.IsSuccess.Should().BeFalse();
        rateLimited.ErrorMessage.Should().Contain("429");

        AiCredentials.ToDto(broken, _protector.Object, 0).KeyDecryptable.Should().BeFalse();
        AiCredentials.ToDto(ok, _protector.Object, 0).KeyDecryptable.Should().BeTrue();
    }
}
