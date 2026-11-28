using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.Quotas.Commands.ReconcileStorage;
using PanelForge.Domain.Entities.Assets;
using PanelForge.Domain.Entities.Bible;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Entities.Identity;
using PanelForge.Domain.Entities.Workspaces;
using PanelForge.Domain.Enums;
using PanelForge.Infrastructure.Persistence;
using PanelForge.UnitTests.Common;
using Xunit;

namespace PanelForge.UnitTests.Application.Quotas;

/// <summary>OPS-22: đối soát dung lượng với kho file, báo cáo file mồ côi, không xoá (C6).</summary>
public class ReconcileStorageTests
{
    private sealed class ListOnlyStorage(Dictionary<string, long> files) : IAssetStorage
    {
        public Task SaveAsync(string key, byte[] content, AssetObjectInfo? info, CancellationToken ct = default)
            => throw new InvalidOperationException("Đối soát không được ghi file.");
        public Task<Stream?> OpenReadAsync(string key, CancellationToken ct = default) => Task.FromResult<Stream?>(null);
        public async IAsyncEnumerable<StoredObject> ListAsync(string prefix, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            foreach (var (key, size) in files.Where(f => f.Key.StartsWith(prefix, StringComparison.Ordinal)))
                yield return new StoredObject(key, size);
            await Task.CompletedTask;
        }
    }

    private static string Sha(char c) => new(c, 64);

    private static async Task<(PanelForgeDbContext Db, StudioWorkspace Ws, Dictionary<string, long> Files)> SeedAsync()
    {
        var db = InMemoryDb.Create();
        var producer = User.Create("p@test.com", "Producer");
        var ws = StudioWorkspace.Create("Studio A", producer.Id, 1_000_000);
        ws.TryReserveStorage(9_999); // số đang ghi lệch với thực tế
        var series = Series.Create(ws.Id, "Truyện", ReadingDirection.RightToLeft);
        series.SetCover(Sha('c'));
        var asset = Asset.Create(ws.Id, Sha('a'), 100, "image/png");
        db.Users.Add(producer);
        db.StudioWorkspaces.Add(ws);
        db.Series.Add(series);
        db.Assets.Add(asset);
        await db.SaveChangesAsync();

        var files = new Dictionary<string, long>
        {
            [asset.StorageKey] = 100,
            [$"covers/{series.Id:N}/{Sha('c')}"] = 20,
            [$"covers/{series.Id:N}/{Sha('d')}"] = 30,          // ảnh bìa cũ đã bị thay → mồ côi
            [$"{ws.Id:N}/ee/{Sha('e')}"] = 40,                   // upload lỗi giữa chừng → mồ côi
            [$"thumbs/{asset.StorageKey}.w480.webp"] = 7,        // ảnh thu nhỏ, không tính hạn mức
            [$"{Guid.NewGuid():N}/ff/{Sha('f')}"] = 50,          // Studio không còn tồn tại
        };
        return (db, ws, files);
    }

    [Fact]
    public async Task Reconcile_WithoutApply_ReportsOrphans_AndChangesNothing_OPS22()
    {
        var (db, ws, files) = await SeedAsync();
        await using var _ = db;
        var countBefore = files.Count;

        var result = await new ReconcileStorageCommandHandler(db, new ListOnlyStorage(files))
            .Handle(new ReconcileStorageCommand(Apply: false), CancellationToken.None);

        var report = result.Value!;
        report.Applied.Should().BeFalse();
        report.ScannedFiles.Should().Be(6);
        var row = report.Workspaces.Single();
        row.RecordedBytes.Should().Be(9_999);
        row.ComputedBytes.Should().Be(120, "chỉ tính artwork và ảnh bìa đang dùng");
        row.OrphanFiles.Should().Be(2);
        row.OrphanBytes.Should().Be(70);
        row.DerivedBytes.Should().Be(7);
        row.MissingFiles.Should().Be(0);
        report.UnattributedFiles.Should().Be(1);
        report.UnattributedBytes.Should().Be(50);
        report.OrphanSamples.Should().HaveCount(3);
        files.Should().HaveCount(countBefore, "đối soát không xoá file");
        (await db.StudioWorkspaces.SingleAsync()).UsedStorageBytes.Should().Be(9_999);
    }

    [Fact]
    public async Task Reconcile_WithApply_WritesComputedUsage_AndCountsMissingFiles_OPS22()
    {
        var (db, ws, files) = await SeedAsync();
        await using var _ = db;
        files.Remove(files.Keys.Single(k => k.StartsWith($"{ws.Id:N}/aa/", StringComparison.Ordinal)));

        var result = await new ReconcileStorageCommandHandler(db, new ListOnlyStorage(files))
            .Handle(new ReconcileStorageCommand(Apply: true), CancellationToken.None);

        result.Value!.Applied.Should().BeTrue();
        var row = result.Value.Workspaces.Single();
        row.ComputedBytes.Should().Be(20);
        row.MissingFiles.Should().Be(1, "artwork còn trong DB nhưng kho không có file");
        (await db.StudioWorkspaces.AsNoTracking().SingleAsync()).UsedStorageBytes.Should().Be(20);
    }

    [Fact]
    public async Task Reconcile_BibleImage_ReferencedBySha_IsNotOrphan_OPS22()
    {
        var (db, ws, files) = await SeedAsync();
        await using var _ = db;
        var series = await db.Series.SingleAsync();
        var bible = SeriesBible.Create(series.Id, ws.Id, "Bible");
        db.SeriesBibles.Add(bible);
        await db.SaveChangesAsync();
        var entry = BibleEntry.Create(bible.Id, BibleEntryCategory.Character, "C01", "Kaito", "Nhân vật chính",
            referenceImageUrl: $"/api/series/{series.Id}/bible/assets/{Sha('b')}");
        db.BibleEntries.Add(entry);
        await db.SaveChangesAsync();
        files[$"bible/{series.Id:N}/{Sha('b')}"] = 15;
        files[$"bible/{series.Id:N}/{Sha('9')}"] = 5;

        var result = await new ReconcileStorageCommandHandler(db, new ListOnlyStorage(files))
            .Handle(new ReconcileStorageCommand(Apply: false), CancellationToken.None);

        var row = result.Value!.Workspaces.Single();
        row.ComputedBytes.Should().Be(135);
        row.OrphanFiles.Should().Be(3);
    }
}
