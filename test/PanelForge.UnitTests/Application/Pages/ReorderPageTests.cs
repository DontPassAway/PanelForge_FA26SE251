using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.Pages.Commands.DeletePage;
using PanelForge.Application.Features.Pages.Commands.ReorderPage;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;
using PanelForge.Infrastructure.Persistence;
using PanelForge.UnitTests.Common;
using Xunit;

namespace PanelForge.UnitTests.Application.Pages;

/// <summary>Di chuyển trang: các trang khác dời theo; không được đưa khung của cảnh sau lên trước cảnh trước.</summary>
public class ReorderPageTests
{
    private readonly PanelForgeDbContext _db = InMemoryDb.Create();
    private readonly Guid _chapterId = Guid.NewGuid();
    private readonly Mock<IPageRepository> _repo = new();

    private async Task<List<Page>> SeedPagesAsync(int count)
    {
        var pages = Enumerable.Range(1, count)
            .Select(n => Page.CreateNew(_chapterId, n, LayoutFormat.StandardPage, 1000, 1500, 300)).ToList();
        _db.Pages.AddRange(pages);
        await _db.SaveChangesAsync();
        // Aggregate Marten riêng cho mỗi trang (bản sao cùng số trang)
        foreach (var p in pages)
        {
            var aggregate = Page.CreateNew(_chapterId, p.PageNumber, LayoutFormat.StandardPage, 1000, 1500, 300);
            typeof(Page).GetProperty(nameof(Page.Id))!.SetValue(aggregate, p.Id);
            _repo.Setup(r => r.GetAsync(p.Id, It.IsAny<CancellationToken>())).ReturnsAsync(aggregate);
        }
        return pages;
    }

    private Task<int> NumberOf(Page p) => _db.Pages.Where(x => x.Id == p.Id).Select(x => x.PageNumber).SingleAsync();

    private ReorderPageCommandHandler Handler() => new(_db, _repo.Object, new PassThroughUnitOfWork());

    [Fact]
    public async Task MovingPageUp_ShiftsOthersDown()
    {
        var pages = await SeedPagesAsync(3);

        var result = await Handler().Handle(new ReorderPageCommand(pages[2].Id, 1, Guid.NewGuid()), default);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        (await NumberOf(pages[2])).Should().Be(1);
        (await NumberOf(pages[0])).Should().Be(2);
        (await NumberOf(pages[1])).Should().Be(3);
        _repo.Verify(r => r.SaveAsync(It.IsAny<Page>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task DeletingFirstPage_RenumbersRemainingPages()
    {
        var pages = await SeedPagesAsync(3);
        var user = new Mock<ICurrentUserService>();
        user.SetupGet(u => u.UserId).Returns(Guid.NewGuid());

        var result = await new DeletePageCommandHandler(_db, _repo.Object, new PassThroughUnitOfWork(), user.Object)
            .Handle(new DeletePageCommand(pages[0].Id), default);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        (await NumberOf(pages[1])).Should().Be(1);
        (await NumberOf(pages[2])).Should().Be(2);
    }

    [Fact]
    public async Task NumberBeyondEnd_MovesToLast()
    {
        var pages = await SeedPagesAsync(3);

        var result = await Handler().Handle(new ReorderPageCommand(pages[0].Id, 99, Guid.NewGuid()), default);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        (await NumberOf(pages[0])).Should().Be(3);
        (await NumberOf(pages[1])).Should().Be(1);
    }

    [Fact]
    public async Task MovingLaterSceneBeforeEarlierScene_IsRejected()
    {
        var pages = await SeedPagesAsync(3);
        var scene1 = Scene.Create(_chapterId, 1);
        var scene2 = Scene.Create(_chapterId, 2);
        _db.Scenes.AddRange(scene1, scene2);
        _db.Panels.Add(Panel.Create(pages[0].Id, 1, 1, sceneId: scene1.Id));
        _db.Panels.Add(Panel.Create(pages[2].Id, 1, 1, sceneId: scene2.Id));
        await _db.SaveChangesAsync();

        var result = await Handler().Handle(new ReorderPageCommand(pages[2].Id, 1, Guid.NewGuid()), default);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Cảnh 2");
        (await NumberOf(pages[2])).Should().Be(3);
        _repo.Verify(r => r.SaveAsync(It.IsAny<Page>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task MovingEmptyPageBetweenScenes_IsAllowed()
    {
        var pages = await SeedPagesAsync(3);
        var scene1 = Scene.Create(_chapterId, 1);
        var scene2 = Scene.Create(_chapterId, 2);
        _db.Scenes.AddRange(scene1, scene2);
        _db.Panels.Add(Panel.Create(pages[0].Id, 1, 1, sceneId: scene1.Id));
        _db.Panels.Add(Panel.Create(pages[1].Id, 1, 1, sceneId: scene2.Id));
        await _db.SaveChangesAsync();

        // Trang 3 trống chuyển lên giữa trang cảnh 1 và trang cảnh 2
        var result = await Handler().Handle(new ReorderPageCommand(pages[2].Id, 2, Guid.NewGuid()), default);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        (await NumberOf(pages[2])).Should().Be(2);
        (await NumberOf(pages[1])).Should().Be(3);
    }
}
