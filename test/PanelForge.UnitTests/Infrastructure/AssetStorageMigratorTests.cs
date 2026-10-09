using System.Security.Cryptography;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Infrastructure.Services;

namespace PanelForge.UnitTests.Infrastructure;

public class AssetStorageMigratorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pf-migrate-" + Guid.NewGuid().ToString("N"));

    private sealed class MemoryStorage : IAssetStorage
    {
        public Dictionary<string, byte[]> Items { get; } = new();
        public Task SaveAsync(string key, byte[] content, AssetObjectInfo? info, CancellationToken ct = default)
        {
            Items.TryAdd(key, content);
            return Task.CompletedTask;
        }
        public Task<Stream?> OpenReadAsync(string key, CancellationToken ct = default)
            => Task.FromResult<Stream?>(Items.TryGetValue(key, out var c) ? new MemoryStream(c) : null);
    }

    private string WriteAsset(string workspace, byte[] content, string? nameOverride = null)
    {
        var hash = Convert.ToHexStringLower(SHA256.HashData(content));
        var dir = Path.Combine(_root, workspace, hash[..2]);
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, nameOverride ?? hash), content);
        return $"{workspace}/{hash[..2]}/{nameOverride ?? hash}";
    }

    [Fact]
    public async Task CopiesValidFiles_WithForwardSlashKeys_AndSkipsBadHashAndTmp()
    {
        var good = WriteAsset("ws1", [1, 2, 3]);
        WriteAsset("ws1", [9], nameOverride: new string('0', 64)); // tên không khớp nội dung
        File.WriteAllBytes(Path.Combine(_root, "ws1", "x.tmp"), [5]);
        var target = new MemoryStorage();

        var summary = await AssetStorageMigrator.RunAsync(_root, target, TextWriter.Null);

        Assert.Equal(new AssetStorageMigrator.Summary(Copied: 1, Invalid: 1, Failed: 0), summary);
        Assert.Equal(new byte[] { 1, 2, 3 }, target.Items[good]);
        Assert.Single(target.Items);
    }

    [Fact]
    public async Task DryRun_WritesNothing()
    {
        WriteAsset("ws1", [4, 5]);

        var summary = await AssetStorageMigrator.RunAsync(_root, target: null, TextWriter.Null);

        Assert.Equal(1, summary.Copied);
    }

    [Fact]
    public async Task RunTwice_IsIdempotent()
    {
        WriteAsset("ws1", [7]);
        var target = new MemoryStorage();

        await AssetStorageMigrator.RunAsync(_root, target, TextWriter.Null);
        var second = await AssetStorageMigrator.RunAsync(_root, target, TextWriter.Null);

        Assert.Equal(0, second.Failed);
        Assert.Single(target.Items);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
