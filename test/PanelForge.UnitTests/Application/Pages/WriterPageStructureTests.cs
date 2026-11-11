using FluentAssertions;
using Moq;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.Pages.Commands.CreatePage;
using PanelForge.Application.Features.Panels.Commands.CreatePanel;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Entities.Workspaces;
using PanelForge.Domain.Enums;
using PanelForge.Infrastructure.Persistence;
using PanelForge.UnitTests.Common;
using Xunit;
using DomainSeries = PanelForge.Domain.Entities.Content.Series;

namespace PanelForge.UnitTests.Application.Pages;

/// <summary>Writer dựng khung Trang → Panel khi lưu kịch bản, theo cùng luật sửa ScriptLine (kịch bản chưa đóng băng).</summary>
public class WriterPageStructureTests
{
    private readonly PanelForgeDbContext _db = InMemoryDb.Create();
    private readonly Mock<IPageRepository> _pages = new();
    private readonly Mock<IWorkspaceAuthorizationService> _auth = new();
    private readonly Guid _writerId = Guid.NewGuid();
    private readonly Chapter _chapter;

    public WriterPageStructureTests()
    {
        var producerId = Guid.NewGuid();
        var studio = StudioWorkspace.Create("Studio", producerId);
        _db.StudioWorkspaces.Add(studio);
        _db.WorkspaceMembers.Add(WorkspaceMember.Create(studio.Id, _writerId, WorkspaceRole.Writer));
        var series = DomainSeries.Create(studio.Id, "Lantern", ReadingDirection.RightToLeft);
        _db.Series.Add(series);
        _chapter = Chapter.Create(series.Id, 1, "Lá thư");
        _db.Chapters.Add(_chapter);
        _db.SaveChanges();

        _auth.Setup(a => a.GetMemberRoleAsync(_writerId, studio.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(WorkspaceRole.Writer);
    }

    private CreatePageCommandHandler PageHandler() =>
        new(_db, _pages.Object, new PassThroughUnitOfWork(), _auth.Object);

    [Fact]
    public async Task Writer_CanCreatePageAndPanel_WhileScriptIsOpen()
    {
        var page = await PageHandler().Handle(new CreatePageCommand(_chapter.Id, 1, UserId: _writerId), default);
        page.IsSuccess.Should().BeTrue(page.ErrorMessage);

        var panel = await new CreatePanelCommandHandler(_db, _auth.Object)
            .Handle(new CreatePanelCommand(page.Value!.Id, _writerId, PanelNumber: 1), default);
        panel.IsSuccess.Should().BeTrue(panel.ErrorMessage);
    }

    [Fact]
    public async Task Writer_CannotCreatePage_AfterScriptIsFrozen()
    {
        _chapter.FreezeScript(Guid.NewGuid());
        await _db.SaveChangesAsync();

        var result = await PageHandler().Handle(new CreatePageCommand(_chapter.Id, 1, UserId: _writerId), default);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        _db.Pages.Should().BeEmpty();
    }

    [Fact]
    public async Task Writer_CannotCreatePanel_AfterScriptIsFrozen()
    {
        var page = Page.CreateNew(_chapter.Id, 1, LayoutFormat.StandardPage, 1200, 1800, 300);
        _db.Pages.Add(page);
        _chapter.FreezeScript(Guid.NewGuid());
        await _db.SaveChangesAsync();

        var result = await new CreatePanelCommandHandler(_db, _auth.Object)
            .Handle(new CreatePanelCommand(page.Id, _writerId, PanelNumber: 1), default);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        _db.Panels.Should().BeEmpty();
    }
}
