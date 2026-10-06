using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using PanelForge.Infrastructure.Services;
using Xunit;

namespace PanelForge.UnitTests.Application.Assets;

/// <summary>Hạ tầng của upload artwork: lưu file cục bộ an toàn, link ngắn hạn không sửa được và tự hết hạn (BR-16).</summary>
public sealed class AssetInfrastructureTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "panelforge-assets-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private LocalAssetStorage Storage() => new(new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["AssetStorage:RootPath"] = _root }).Build());

    [Fact]
    public async Task Storage_SaveThenRead_RoundTripsContent()
    {
        var storage = Storage();

        await storage.SaveAsync("ws/ab/abcdef", [1, 2, 3, 4], CancellationToken.None);

        await using var stream = await storage.OpenReadAsync("ws/ab/abcdef", CancellationToken.None);
        stream.Should().NotBeNull();
        using var copy = new MemoryStream();
        await stream!.CopyToAsync(copy);
        copy.ToArray().Should().Equal(1, 2, 3, 4);
    }

    [Fact]
    public async Task Storage_SavingSameKeyTwice_KeepsFirstContent_AndLeavesNoTempFiles()
    {
        var storage = Storage();
        await storage.SaveAsync("ws/ab/key", [1], CancellationToken.None);

        await storage.SaveAsync("ws/ab/key", [9, 9], CancellationToken.None);

        (await File.ReadAllBytesAsync(Path.Combine(_root, "ws", "ab", "key"))).Should().Equal(1);
        Directory.GetFiles(_root, "*.tmp", SearchOption.AllDirectories).Should().BeEmpty();
    }

    [Fact]
    public async Task Storage_MissingKey_ReturnsNull()
    {
        (await Storage().OpenReadAsync("ws/zz/none", CancellationToken.None)).Should().BeNull();
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("ws/../../escape")]
    [InlineData("..")]
    public async Task Storage_KeyEscapingTheRoot_IsRejected(string key)
    {
        var storage = Storage();

        await FluentActions.Awaiting(() => storage.SaveAsync(key, [1], CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>();
        await FluentActions.Awaiting(() => storage.OpenReadAsync(key, CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>();
    }

    private static DataProtectionAssetLinkSigner Signer() => new(new EphemeralDataProtectionProvider());

    [Fact]
    public void Signer_RoundTrips_ReferenceAndUser()
    {
        var signer = Signer();
        var reference = Guid.NewGuid();
        var user = Guid.NewGuid();

        var claims = signer.Validate(signer.Create(reference, user, TimeSpan.FromMinutes(5)));

        claims.Should().NotBeNull();
        claims!.AssetReferenceId.Should().Be(reference);
        claims.UserId.Should().Be(user);
    }

    [Fact]
    public void Signer_ExpiredToken_IsRejected()
    {
        var signer = Signer();

        var token = signer.Create(Guid.NewGuid(), Guid.NewGuid(), TimeSpan.FromSeconds(-1));

        signer.Validate(token).Should().BeNull();
    }

    [Fact]
    public void Signer_TamperedOrForeignToken_IsRejected()
    {
        var signer = Signer();
        var token = signer.Create(Guid.NewGuid(), Guid.NewGuid(), TimeSpan.FromMinutes(5));
        var tampered = token[..^3] + (token.EndsWith("AAA") ? "BBB" : "AAA");

        signer.Validate(tampered).Should().BeNull();
        signer.Validate("not-a-token").Should().BeNull();
        signer.Validate("").Should().BeNull();
        Signer().Validate(token).Should().BeNull(); // khoá khác (provider khác) không đọc được
    }
}
