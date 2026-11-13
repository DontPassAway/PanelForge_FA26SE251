using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.FrameTemplates.Commands.ApplyFrameTemplate;
using PanelForge.Application.Features.FrameTemplates.Commands.CreateFrameTemplate;
using PanelForge.Application.Features.FrameTemplates.Queries.GetFrameTemplates;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;
using PanelForge.Infrastructure.Persistence;
using PanelForge.UnitTests.Common;
using Xunit;

namespace PanelForge.UnitTests.Application.FrameTemplates;

public class FrameTemplateTests
{
    private static readonly Guid FourGrid = Guid.Parse("f0000000-0000-0000-0000-000000000005");
    private static readonly Guid SpreadTemplate = Guid.Parse("f0000000-0000-0000-0000-00000000000b");
    private readonly Guid _user = Guid.NewGuid();

    private static async Task<(PanelForgeDbContext db, Page page, Guid workspaceId)> Seed(ReadingDirection dir)
    {
        var db = InMemoryDb.Create();
        var workspaceId = Guid.NewGuid();
        var series = Series.Create(workspaceId, "Manga", dir);
        var chapter = Chapter.Create(series.Id, 1, "Ch 1").InProduction();
        var page = Page.CreateNew(chapter.Id, 1, LayoutFormat.StandardPage, 1000, 1500, 300);
        db.Series.Add(series);
        db.Chapters.Add(chapter);
        db.Pages.Add(page);
        await db.SaveChangesAsync();
        return (db, page, workspaceId);
    }

    private static double NormX(Panel p) =>
        JsonDocument.Parse(p.BoundingBox).RootElement.GetProperty("normalized").GetProperty("x").GetDouble();

    [Fact]
    public void BuiltIns_AllHaveValidGeometry_AndUniqueIds()
    {
        FrameTemplate.BuiltIns.Should().OnlyHaveUniqueItems(t => t.Id);
        foreach (var t in FrameTemplate.BuiltIns)
            FluentActions.Invoking(() => FrameTemplate.ValidateSlots(t.Slots)).Should().NotThrow(t.Name);
    }

    [Theory]
    [InlineData(ReadingDirection.RightToLeft, true)]
    [InlineData(ReadingDirection.LeftToRight, false)]
    public async Task Apply_FourGrid_CreatesPanels_InReadingDirectionOrder(ReadingDirection dir, bool rightFirst)
    {
        var (db, page, _) = await Seed(dir);

        var result = await new ApplyFrameTemplateCommandHandler(db).Handle(new ApplyFrameTemplateCommand(page.Id, FourGrid, _user), default);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        var panels = await db.Panels.Where(p => p.PageId == page.Id).OrderBy(p => p.ReadingOrder).ToListAsync();
        panels.Should().HaveCount(4);
        // Panel 1 và 2 cùng hàng trên; manga (RTL) đọc khung bên phải trước
        (NormX(panels[0]) > NormX(panels[1])).Should().Be(rightFirst);
        panels[0].BoundingBox.Should().Contain("\"frameTemplate\"").And.Contain("\"unit\":\"px\"");
    }

    [Fact]
    public async Task Apply_WhenPageAlreadyHasPanels_ShouldConflict()
    {
        var (db, page, _) = await Seed(ReadingDirection.RightToLeft);
        db.Panels.Add(Panel.Create(page.Id, 1, 1, "{}"));
        await db.SaveChangesAsync();

        var result = await new ApplyFrameTemplateCommandHandler(db).Handle(new ApplyFrameTemplateCommand(page.Id, FourGrid, _user), default);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        (await db.Panels.CountAsync(p => p.PageId == page.Id)).Should().Be(1);
    }

    [Fact]
    public async Task Apply_SpreadTemplateOnStandardPage_ShouldBeRejected()
    {
        var (db, page, _) = await Seed(ReadingDirection.RightToLeft);

        var result = await new ApplyFrameTemplateCommandHandler(db).Handle(new ApplyFrameTemplateCommand(page.Id, SpreadTemplate, _user), default);

        result.ErrorCode.Should().Be(ResultErrorCodes.Validation);
    }

    [Fact]
    public async Task Apply_TemplateOfAnotherWorkspace_ShouldBeNotFound()
    {
        var (db, page, _) = await Seed(ReadingDirection.RightToLeft);
        var foreign = FrameTemplate.Create(Guid.NewGuid(), "Khác", LayoutFormat.StandardPage, [new FrameSlot(0, 0, 1, 1)]);
        db.FrameTemplates.Add(foreign);
        await db.SaveChangesAsync();

        var result = await new ApplyFrameTemplateCommandHandler(db).Handle(new ApplyFrameTemplateCommand(page.Id, foreign.Id, _user), default);

        result.ErrorCode.Should().Be(ResultErrorCodes.NotFound);
    }

    [Fact]
    public async Task Create_ThenList_ReturnsBuiltInsFollowedByWorkspaceTemplate()
    {
        var (db, _, workspaceId) = await Seed(ReadingDirection.RightToLeft);

        var created = await new CreateFrameTemplateCommandHandler(db).Handle(new CreateFrameTemplateCommand(
            workspaceId, _user, "Bố cục của tôi", LayoutFormat.StandardPage,
            [new FrameSlot(0.05, 0.05, 0.9, 0.4), new FrameSlot(0.05, 0.5, 0.9, 0.45, Borderless: true)]), default);
        var list = await new GetFrameTemplatesQueryHandler(db).Handle(new GetFrameTemplatesQuery(workspaceId), default);

        created.IsSuccess.Should().BeTrue(created.ErrorMessage);
        list.Should().HaveCount(FrameTemplate.BuiltIns.Count + 1);
        list[^1].Name.Should().Be("Bố cục của tôi");
        list[^1].Slots[1].Borderless.Should().BeTrue();
        (await new GetFrameTemplatesQueryHandler(db).Handle(new GetFrameTemplatesQuery(Guid.NewGuid()), default))
            .Should().HaveCount(FrameTemplate.BuiltIns.Count);
    }

    [Fact]
    public async Task Create_WithSlotOutsidePage_ShouldFailValidation()
    {
        var (db, _, workspaceId) = await Seed(ReadingDirection.RightToLeft);

        var result = await new CreateFrameTemplateCommandHandler(db).Handle(new CreateFrameTemplateCommand(
            workspaceId, _user, "Sai", LayoutFormat.StandardPage, [new FrameSlot(0.5, 0, 0.6, 1)]), default);

        result.ErrorCode.Should().Be(ResultErrorCodes.Validation);
    }
}
