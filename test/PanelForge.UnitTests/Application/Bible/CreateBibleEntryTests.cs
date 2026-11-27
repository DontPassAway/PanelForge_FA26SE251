using FluentAssertions;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Bible.Commands.CreateBibleEntry;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;
using PanelForge.Infrastructure.Persistence;
using PanelForge.UnitTests.Common;
using Xunit;

namespace PanelForge.UnitTests.Application.Bible;

public sealed class CreateBibleEntryTests
{
    [Fact]
    public async Task Create_DuplicateNormalizedName_ReturnsConflict()
    {
        await using PanelForgeDbContext db = InMemoryDb.Create();
        var workspaceId = Guid.NewGuid();
        var series = Series.Create(workspaceId, "Series", ReadingDirection.RightToLeft);
        db.Series.Add(series);
        await db.SaveChangesAsync();

        var handler = new CreateBibleEntryCommandHandler(db);
        var first = await handler.Handle(new CreateBibleEntryCommand(
            SeriesId: series.Id, UserId: Guid.NewGuid(), Category: BibleEntryCategory.Character,
            Name: "Hắc Long", Description: "Nhân vật"), default);
        var duplicate = await handler.Handle(new CreateBibleEntryCommand(
            SeriesId: series.Id, UserId: Guid.NewGuid(), Category: BibleEntryCategory.Character,
            Name: "  HẮC   LONG  ", Description: "Nhân vật khác"), default);

        first.IsSuccess.Should().BeTrue();
        duplicate.IsSuccess.Should().BeFalse();
        duplicate.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
    }
}
