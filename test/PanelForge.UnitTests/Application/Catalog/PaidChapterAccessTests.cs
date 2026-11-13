using FluentAssertions;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Catalog;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Entities.Identity;
using PanelForge.Domain.Entities.Payments;
using PanelForge.Domain.Entities.Workspaces;
using PanelForge.Domain.Enums;
using PanelForge.Infrastructure.Persistence;
using PanelForge.UnitTests.Common;
using Xunit;
using DomainSeries = PanelForge.Domain.Entities.Content.Series;

namespace PanelForge.UnitTests.Application.Catalog;

/// <summary>
/// Chương trả phí (IsLocked) trên API catalog công khai: chỉ Publisher/Admin, thành viên Studio, VIP còn hạn
/// hoặc người đã mua mới đọc được toàn bộ (IsUnlocked); còn lại chỉ thấy trang xem trước (xem PreviewPageAccessTests).
/// </summary>
public class PaidChapterAccessTests
{
    private readonly PanelForgeDbContext _db = InMemoryDb.Create();
    private readonly StudioWorkspace _studio;
    private readonly DomainSeries _series;
    private readonly Chapter _paid;

    public PaidChapterAccessTests()
    {
        _studio = StudioWorkspace.Create("Studio Moon", Guid.NewGuid());
        _db.StudioWorkspaces.Add(_studio);
        _series = DomainSeries.Create(_studio.Id, "Moon Blade", ReadingDirection.RightToLeft, "Synopsis", "Fantasy");
        _db.Series.Add(_series);

        _paid = Chapter.Create(_series.Id, 2, "Trả phí");
        _paid.SetPricing(true, 10000m);
        typeof(Chapter).GetProperty(nameof(Chapter.Status))!.SetValue(_paid, ChapterStatus.Published);
        typeof(Chapter).GetProperty(nameof(Chapter.PublishedAt))!.SetValue(_paid, DateTime.UtcNow.AddDays(-1));
        _db.Chapters.Add(_paid);
        _db.SaveChanges();
    }

    private Task<Result<PublicChapterReadDto>> Read(Guid? userId) =>
        new GetPublicChapterQueryHandler(_db, new FakeCurrentUser(userId))
            .Handle(new GetPublicChapterQuery(_paid.Id), default);

    private Guid AddUser(SystemRole role = SystemRole.User)
    {
        var user = User.Create($"{Guid.NewGuid():N}@test.local", "Reader", role: role);
        _db.Users.Add(user);
        _db.SaveChanges();
        return user.Id;
    }

    [Fact]
    public async Task Guest_SeesChapterAsNotUnlocked()
    {
        var result = await Read(null);

        result.IsSuccess.Should().BeTrue();
        result.Value!.IsUnlocked.Should().BeFalse();
    }

    [Fact]
    public async Task ReaderWithoutPurchase_IsNotUnlocked()
    {
        (await Read(AddUser())).Value!.IsUnlocked.Should().BeFalse();
    }

    [Fact]
    public async Task ReaderWithEntitlement_CanRead()
    {
        var reader = AddUser();
        _db.ChapterEntitlements.Add(ChapterEntitlement.Create(reader, _paid.Id, _series.Id, _studio.Id, 10000m));
        await _db.SaveChangesAsync();

        var result = await Read(reader);

        result.IsSuccess.Should().BeTrue();
        result.Value!.IsUnlocked.Should().BeTrue();
    }

    [Fact]
    public async Task ActiveVip_CanRead_ExpiredVip_Cannot()
    {
        var vip = AddUser();
        var expired = AddUser();
        _db.ReaderWallets.AddRange(
            ReaderWallet.Create(vip, 0m, isVip: true, vipExpiresAt: DateTime.UtcNow.AddDays(5)),
            ReaderWallet.Create(expired, 0m, isVip: true, vipExpiresAt: DateTime.UtcNow.AddDays(-1)));
        await _db.SaveChangesAsync();

        (await Read(vip)).Value!.IsUnlocked.Should().BeTrue();
        (await Read(expired)).Value!.IsUnlocked.Should().BeFalse();
    }

    [Fact]
    public async Task SystemPublisher_CanRead()
    {
        (await Read(AddUser(SystemRole.Publisher))).Value!.IsUnlocked.Should().BeTrue();
    }

    [Fact]
    public async Task StudioMember_CanRead()
    {
        var artist = AddUser();
        _db.WorkspaceMembers.Add(WorkspaceMember.Create(_studio.Id, artist, WorkspaceRole.Artist));
        await _db.SaveChangesAsync();

        (await Read(artist)).Value!.IsUnlocked.Should().BeTrue();
    }

    [Fact]
    public async Task SeriesDetail_MarksChapterUnlockedForStudioMember()
    {
        var producer = AddUser();
        _db.WorkspaceMembers.Add(WorkspaceMember.Create(_studio.Id, producer, WorkspaceRole.Producer));
        await _db.SaveChangesAsync();

        var detail = await new GetPublicSeriesDetailQueryHandler(_db, new FakeCurrentUser(producer))
            .Handle(new GetPublicSeriesDetailQuery(_series.Id), default);

        detail.Value!.Chapters.Single().IsUnlocked.Should().BeTrue();
    }
}
