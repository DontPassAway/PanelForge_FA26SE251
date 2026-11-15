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
            series.Id, Guid.NewGuid(), BibleEntryCategory.Character,
            "CHR_1", "Hắc Long", "Nhân vật"), default);
        var duplicate = await handler.Handle(new CreateBibleEntryCommand(
            series.Id, Guid.NewGuid(), BibleEntryCategory.Character,
            "CHR_2", "  HẮC   LONG  ", "Nhân vật khác"), default);

        first.IsSuccess.Should().BeTrue();
        duplicate.IsSuccess.Should().BeFalse();
        duplicate.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
    }
}
