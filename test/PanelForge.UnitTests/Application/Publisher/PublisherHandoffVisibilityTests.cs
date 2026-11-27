using FluentAssertions;
using Moq;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.Publisher.Monetization;
using PanelForge.Application.Features.Publisher.Queries.GetPublisherChapters;
using PanelForge.Domain.Entities.Assets;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Entities.Workspaces;
using PanelForge.Domain.Enums;
using PanelForge.Infrastructure.Persistence;
using PanelForge.UnitTests.Common;
using Xunit;
using DomainSeries = PanelForge.Domain.Entities.Content.Series;

namespace PanelForge.UnitTests.Application.Publisher;

/// <summary>
/// AUD-BE-09: Publisher chỉ thấy chương đã được Producer bàn giao (IsHandedOff == true và
/// ChapterApproved | HandedOffAndPublished), cả ở danh sách chương lẫn ảnh trang xem trước.
/// </summary>
public class PublisherHandoffVisibilityTests
{
    private readonly PanelForgeDbContext _db = InMemoryDb.Create();
    private readonly Guid _producerId = Guid.NewGuid();
    private readonly StudioWorkspace _studio;
    private readonly DomainSeries _series;

    public PublisherHandoffVisibilityTests()
    {
        _studio = StudioWorkspace.Create("Studio Moon", _producerId);
        _db.StudioWorkspaces.Add(_studio);
        _series = DomainSeries.Create(_studio.Id, "Moon Blade", ReadingDirection.RightToLeft, genre: "Fantasy");
        _db.Series.Add(_series);
        _db.SaveChanges();
    }

    [Fact]
    public async Task GetPublisherChapters_ReturnsOnlyHandedOffApprovedOrPublishedChapters()
    {
        var preProd = Chapter.Create(_series.Id, 1, "PreProd");
        var inProd = Chapter.Create(_series.Id, 2, "InProd").InProduction();
        var approvedNotHandedOff = Chapter.Create(_series.Id, 3, "ApprovedNotHandedOff").InProduction();
        approvedNotHandedOff.Approve();

        var handedOffApproved = Chapter.Create(_series.Id, 4, "HandedOffApproved").InProduction();
        handedOffApproved.Approve();
        handedOffApproved.HandOff(_producerId);

        var published = Chapter.Create(_series.Id, 5, "Published").InProduction();
        published.Approve();
        published.HandOff(_producerId);
        published.Publish(DateTime.UtcNow.AddMinutes(-5));

        _db.Chapters.AddRange(preProd, inProd, approvedNotHandedOff, handedOffApproved, published);
        await _db.SaveChangesAsync();

        var handler = new GetPublisherChaptersQueryHandler(_db);

        // Default ("all") must only include handedOffApproved and published
        var all = await handler.Handle(new GetPublisherChaptersQuery(), default);
        all.IsSuccess.Should().BeTrue();
        all.Value!.Items.Select(c => c.Id).Should().BeEquivalentTo([handedOffApproved.Id, published.Id]);

        // Even requesting status=InProduction must return empty
        var inProdFilter = await handler.Handle(new GetPublisherChaptersQuery(Status: "InProduction"), default);
        inProdFilter.IsSuccess.Should().BeTrue();
        inProdFilter.Value!.Items.Should().BeEmpty();

        // Requesting ChapterApproved returns only the handed-off one
        var approvedFilter = await handler.Handle(new GetPublisherChaptersQuery(Status: "ChapterApproved"), default);
        approvedFilter.IsSuccess.Should().BeTrue();
        approvedFilter.Value!.Items.Select(c => c.Id).Should().Equal(handedOffApproved.Id);
    }

    [Fact]
    public async Task GetPublisherPageImage_ForNonHandedOffChapter_ReturnsNotFound_AndSucceedsWhenHandedOff()
    {
        var pngBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00 };
        var stage = PanelForge.Domain.Entities.Workflow.PipelineStage.Create(_series.Id, "final", "Final", 1);
        _db.PipelineStages.Add(stage);
        var asset = Asset.Create(_studio.Id, new string('b', 64), pngBytes.Length, "image/png");
        _db.Assets.Add(asset);

        var inProd = Chapter.Create(_series.Id, 1, "InProd").InProduction();
        var inProdPage = Page.CreateNew(inProd.Id, 1, default, 800, 1200, 300);

        var approvedOnly = Chapter.Create(_series.Id, 2, "ApprovedOnly").InProduction();
        approvedOnly.Approve();
        var approvedOnlyPage = Page.CreateNew(approvedOnly.Id, 1, default, 800, 1200, 300);

        var handedOff = Chapter.Create(_series.Id, 3, "HandedOff").InProduction();
        handedOff.Approve();
        handedOff.HandOff(_producerId);
        var handedOffPage = Page.CreateNew(handedOff.Id, 1, default, 800, 1200, 300);

        _db.Chapters.AddRange(inProd, approvedOnly, handedOff);
        _db.Pages.AddRange(inProdPage, approvedOnlyPage, handedOffPage);
        _db.AssetReferences.AddRange(
            AssetReference.Create(asset.Id, _studio.Id, inProd.Id, inProdPage.Id, stage.Id, 1, null, "p1.png", _producerId),
            AssetReference.Create(asset.Id, _studio.Id, approvedOnly.Id, approvedOnlyPage.Id, stage.Id, 1, null, "p2.png", _producerId),
            AssetReference.Create(asset.Id, _studio.Id, handedOff.Id, handedOffPage.Id, stage.Id, 1, null, "p3.png", _producerId));
        await _db.SaveChangesAsync();

        var storage = new Mock<IAssetStorage>();
        storage.Setup(s => s.OpenReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream(pngBytes));
        var pages = new Mock<IPageRepository>();
        var compositor = new Mock<IPageLetteringCompositor>();

        var handler = new GetPublisherPageImageQueryHandler(_db, storage.Object, pages.Object, compositor.Object);

        // InProduction -> 404 NotFound
        var r1 = await handler.Handle(new(inProd.Id, inProdPage.Id, "vi"), default);
        r1.IsSuccess.Should().BeFalse();
        r1.ErrorCode.Should().Be(ResultErrorCodes.NotFound);

        // ChapterApproved without HandOff -> 404 NotFound
        var r2 = await handler.Handle(new(approvedOnly.Id, approvedOnlyPage.Id, "vi"), default);
        r2.IsSuccess.Should().BeFalse();
        r2.ErrorCode.Should().Be(ResultErrorCodes.NotFound);

        // HandedOff -> 200 Success
        var r3 = await handler.Handle(new(handedOff.Id, handedOffPage.Id, "vi"), default);
        r3.IsSuccess.Should().BeTrue();
        r3.Value.Should().NotBeNull();
    }
}
