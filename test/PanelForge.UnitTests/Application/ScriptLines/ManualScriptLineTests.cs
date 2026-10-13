using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.ScriptLines.Commands.CreateScriptLine;
using PanelForge.Application.Features.ScriptLines.Commands.DeleteScriptLine;
using PanelForge.Application.Features.ScriptLines.Commands.ReorderScriptLines;
using PanelForge.Application.Features.ScriptLines.Commands.UpdateScriptLine;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Entities.Workspaces;
using PanelForge.Domain.Enums;
using PanelForge.Infrastructure.Persistence;
using PanelForge.UnitTests.Common;
using Xunit;
using DomainSeries = PanelForge.Domain.Entities.Content.Series;

namespace PanelForge.UnitTests.Application.ScriptLines;

/// <summary>UC-06: Writer viết script thủ công. Quyền, khoá chương, xung đột version, lịch sử revision, chặn IDOR.</summary>
public class ManualScriptLineTests
{
    private readonly PanelForgeDbContext _db = InMemoryDb.Create();
    private readonly Mock<IWorkspaceAuthorizationService> _auth = new();
    private readonly StudioWorkspace _studio = StudioWorkspace.Create("Studio", Guid.NewGuid());
    private readonly DomainSeries _series;
    private readonly Chapter _chapter;
    private readonly Scene _scene;
    private readonly Guid _writer = Guid.NewGuid();

    public ManualScriptLineTests()
    {
        _series = DomainSeries.Create(_studio.Id, "Moon", ReadingDirection.RightToLeft);
        _chapter = Chapter.Create(_series.Id, 1);
        _scene = Scene.Create(_chapter.Id, 1);
        _db.StudioWorkspaces.Add(_studio);
        _db.Series.Add(_series);
        _db.Chapters.Add(_chapter);
        _db.Scenes.Add(_scene);
        _db.SaveChanges();
        _auth.Setup(a => a.GetMemberRoleAsync(_writer, _studio.Id, It.IsAny<CancellationToken>())).ReturnsAsync(WorkspaceRole.Writer);
    }

    private Task<Result<PanelForge.Application.Features.ScriptLines.Models.ScriptLineDto>> Create(
        string? dialogue = "Xin chào", string? direction = null, int? order = null, long? expected = null, Guid? speaker = null,
        Guid? chapterId = null, Guid? sceneId = null) =>
        new CreateScriptLineCommandHandler(_db, _auth.Object).Handle(
            new CreateScriptLineCommand(chapterId ?? _chapter.Id, sceneId ?? _scene.Id, _writer, dialogue, direction, speaker, order, expected), default);

    [Fact]
    public async Task Create_AppendsLine_RecordsRevision_AndBumpsChapterVersion()
    {
        var first = await Create("Dòng 1");
        var second = await Create("Dòng 2");

        first.IsSuccess.Should().BeTrue();
        (first.Value!.LineOrder, second.Value!.LineOrder).Should().Be((1, 2));
        (first.Value.ChapterVersion, second.Value.ChapterVersion).Should().Be((1, 2));
        var revisions = _db.ContentRevisions.Where(r => r.SceneId == _scene.Id && r.Kind == ContentRevisionKind.Script)
            .OrderBy(r => r.VersionNumber).ToList();
        revisions.Select(r => r.VersionNumber).Should().Equal(1, 2);
        revisions[1].PreviousRevisionId.Should().Be(revisions[0].Id);
        revisions[1].SourceSuggestionId.Should().BeNull("sửa tay không đến từ đề xuất AI");
    }

    [Fact]
    public async Task Create_WhenSceneAlreadyHasLinesWithoutRevision_CapturesBaselineFirst()
    {
        _db.ScriptLines.Add(ScriptLine.Create(_scene.Id, 1, "Có sẵn"));
        await _db.SaveChangesAsync();

        await Create("Mới");

        _db.ContentRevisions.Where(r => r.SceneId == _scene.Id).OrderBy(r => r.VersionNumber)
            .Select(r => r.VersionNumber).Should().Equal(1, 2);
        _db.ContentRevisions.First(r => r.VersionNumber == 1).Summary.Should().StartWith("Baseline");
    }

    [Fact]
    public async Task Create_AtExistingPosition_ShiftsFollowingLines()
    {
        await Create("A"); await Create("B");

        var inserted = await Create("Chen", order: 1);

        inserted.Value!.LineOrder.Should().Be(1);
        _db.ScriptLines.Where(l => l.SceneId == _scene.Id).OrderBy(l => l.LineOrder)
            .Select(l => l.DialogueText).Should().Equal("Chen", "A", "B");
    }

    [Fact]
    public async Task Create_RejectsEmptyContent_AndUnknownSpeaker()
    {
        (await Create("  ", null)).ErrorCode.Should().Be(ResultErrorCodes.Validation);
        (await Create("Có thoại", speaker: Guid.NewGuid())).ErrorCode.Should().Be(ResultErrorCodes.Validation);
        _db.ScriptLines.Should().BeEmpty();
    }

    [Fact]
    public async Task Create_StageDirectionOnly_IsAllowed()
    {
        var r = await Create(null, "Mưa rơi trên mái nhà");

        r.IsSuccess.Should().BeTrue();
        r.Value!.DialogueText.Should().BeNull();
        r.Value.StageDirection.Should().Be("Mưa rơi trên mái nhà");
    }

    [Fact]
    public async Task Create_WithStaleExpectedVersion_ConflictsAndChangesNothing()
    {
        await Create("A");

        var r = await Create("B", expected: 0);

        r.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        _db.ScriptLines.Count(l => l.SceneId == _scene.Id).Should().Be(1);
    }

    [Fact]
    public async Task Create_AfterChapterApproved_Conflicts()
    {
        _chapter.Approve();
        await _db.SaveChangesAsync();

        (await Create()).ErrorCode.Should().Be(ResultErrorCodes.Conflict);
    }

    [Fact]
    public async Task Create_ByWriterWhenChapterAssignedToAnotherWriter_IsForbidden()
    {
        _db.Assignments.Add(Assignment.Create(_series.Id, _chapter.Id, WorkflowEntityType.Chapter, _chapter.Id,
            Guid.NewGuid(), Guid.NewGuid(), WorkspaceRole.Writer, "Viết kịch bản"));
        await _db.SaveChangesAsync();

        (await Create()).ErrorCode.Should().Be(ResultErrorCodes.Forbidden);
    }

    [Fact]
    public async Task Create_InSceneOfAnotherChapter_IsNotFound()
    {
        var other = Chapter.Create(_series.Id, 2);
        var otherScene = Scene.Create(other.Id, 1);
        _db.Chapters.Add(other);
        _db.Scenes.Add(otherScene);
        await _db.SaveChangesAsync();

        (await Create(sceneId: otherScene.Id)).ErrorCode.Should().Be(ResultErrorCodes.NotFound);
    }

    [Fact]
    public async Task Update_ReplacesContent_AndKeepsOldTextInPreviousRevision()
    {
        var created = await Create("Bản đầu");

        var updated = await new UpdateScriptLineCommandHandler(_db, _auth.Object).Handle(
            new UpdateScriptLineCommand(_chapter.Id, created.Value!.Id, _writer, "Bản sửa", null, null, created.Value.ChapterVersion), default);

        updated.IsSuccess.Should().BeTrue();
        updated.Value!.DialogueText.Should().Be("Bản sửa");
        updated.Value.ChapterVersion.Should().Be(2);
        _db.ContentRevisions.OrderBy(r => r.VersionNumber).First().PayloadJson.Should().Contain("Bản đầu");
        _db.ContentRevisions.OrderBy(r => r.VersionNumber).Last().PayloadJson.Should().Contain("Bản sửa").And.NotContain("Bản đầu");
    }

    [Fact]
    public async Task Update_LineOfAnotherChapter_IsNotFound()
    {
        var created = await Create("A");
        var other = Chapter.Create(_series.Id, 2);
        _db.Chapters.Add(other);
        await _db.SaveChangesAsync();

        var r = await new UpdateScriptLineCommandHandler(_db, _auth.Object).Handle(
            new UpdateScriptLineCommand(other.Id, created.Value!.Id, _writer, "Hack", null, null, null), default);

        r.ErrorCode.Should().Be(ResultErrorCodes.NotFound);
    }

    [Fact]
    public async Task Delete_SoftDeletesLine_AndRevisionOmitsIt()
    {
        var a = await Create("A"); var b = await Create("B");

        var r = await new DeleteScriptLineCommandHandler(_db, _auth.Object).Handle(
            new DeleteScriptLineCommand(_chapter.Id, a.Value!.Id, _writer, null), default);

        r.IsSuccess.Should().BeTrue();
        r.Value!.ChapterVersion.Should().Be(3);
        _db.ScriptLines.Where(l => l.SceneId == _scene.Id).Select(l => l.Id).Should().Equal(b.Value!.Id);
        _db.ContentRevisions.OrderBy(r2 => r2.VersionNumber).Last().PayloadJson.Should().NotContain("\"A\"").And.Contain("\"B\"");
    }

    // ── UC-06: liên kết dòng script với mục Series Bible ──────────────────────

    private PanelForge.Domain.Entities.Bible.BibleEntry AddBibleEntry(Guid seriesId, BibleEntryCategory category, string code)
    {
        var bible = _db.SeriesBibles.FirstOrDefault(b => b.SeriesId == seriesId);
        if (bible is null)
        {
            bible = PanelForge.Domain.Entities.Bible.SeriesBible.Create(seriesId, _studio.Id, "Bible");
            _db.SeriesBibles.Add(bible);
        }
        var entry = PanelForge.Domain.Entities.Bible.BibleEntry.Create(bible.Id, category, code, code, "mô tả");
        _db.BibleEntries.Add(entry);
        _db.SaveChanges();
        return entry;
    }

    private Task<Result<PanelForge.Application.Features.ScriptLines.Models.ScriptLineDto>> Update(Guid lineId, IReadOnlyList<Guid>? links) =>
        new UpdateScriptLineCommandHandler(_db, _auth.Object).Handle(
            new UpdateScriptLineCommand(_chapter.Id, lineId, _writer, "Sửa", null, null, null, links), default);

    [Fact]
    public async Task BibleLinks_CreateUpdateKeepAndClear()
    {
        var key = AddBibleEntry(_series.Id, BibleEntryCategory.Prop, "KEY");
        var bar = AddBibleEntry(_series.Id, BibleEntryCategory.Location, "BAR");

        var created = await new CreateScriptLineCommandHandler(_db, _auth.Object).Handle(
            new CreateScriptLineCommand(_chapter.Id, _scene.Id, _writer, "An cầm chìa khoá", null, null, null, null, [key.Id, key.Id]), default);
        created.Value!.BibleEntryIds.Should().Equal(key.Id);

        (await Update(created.Value.Id, [bar.Id])).Value!.BibleEntryIds.Should().Equal(bar.Id);
        (await Update(created.Value.Id, null)).Value!.BibleEntryIds.Should().Equal([bar.Id], "null = giữ liên kết hiện có");
        (await Update(created.Value.Id, [])).Value!.BibleEntryIds.Should().BeEmpty();

        _db.ScriptLineBibleLinks.Should().BeEmpty("liên kết đã gỡ bị xoá mềm, không còn hoạt động");
        _db.ScriptLineBibleLinks.IgnoreQueryFilters().Should().HaveCount(2, "lịch sử liên kết được giữ");
    }

    [Fact]
    public async Task Create_ReplayedWithSameClientLineId_ReturnsExisting_WithoutNewRevision()
    {
        var clientId = Guid.NewGuid();
        Task<Result<PanelForge.Application.Features.ScriptLines.Models.ScriptLineDto>> Send(long? expected) =>
            new CreateScriptLineCommandHandler(_db, _auth.Object).Handle(
                new CreateScriptLineCommand(_chapter.Id, _scene.Id, _writer, "Offline", null, null, null, expected, null, clientId), default);

        var first = await Send(0);
        var replay = await Send(0); // cùng expectedChapterVersion cũ: lần đầu đã tăng version nhưng gửi lại vẫn thành công

        first.Value!.Id.Should().Be(clientId);
        replay.IsSuccess.Should().BeTrue(replay.ErrorMessage);
        replay.Value!.Replayed.Should().BeTrue();
        replay.Value.RevisionId.Should().Be(first.Value.RevisionId);
        _db.ScriptLines.Should().ContainSingle();
        _db.ContentRevisions.Should().ContainSingle();
    }

    [Fact]
    public async Task BibleLinks_ToEntryOfAnotherSeries_AreRejected()
    {
        var otherSeries = DomainSeries.Create(_studio.Id, "Khác", ReadingDirection.RightToLeft);
        _db.Series.Add(otherSeries);
        var foreign = AddBibleEntry(otherSeries.Id, BibleEntryCategory.Prop, "X");

        var r = await new CreateScriptLineCommandHandler(_db, _auth.Object).Handle(
            new CreateScriptLineCommand(_chapter.Id, _scene.Id, _writer, "A", null, null, null, null, [foreign.Id]), default);

        r.ErrorCode.Should().Be(ResultErrorCodes.Validation);
        _db.ScriptLines.Should().BeEmpty();
    }

    [Fact]
    public async Task ReorderScriptLines_SuccessfullyUpdatesOrderAndRecordsRevision()
    {
        var line1 = ScriptLine.Create(_scene.Id, 1, "Thoại 1", null);
        var line2 = ScriptLine.Create(_scene.Id, 2, "Thoại 2", null);
        _db.ScriptLines.AddRange(line1, line2);
        await _db.SaveChangesAsync();

        var handler = new ReorderScriptLinesCommandHandler(_db, _auth.Object);
        var items = new List<ReorderScriptLineItem>
        {
            new(line1.Id, 2),
            new(line2.Id, 1)
        };

        var result = await handler.Handle(
            new ReorderScriptLinesCommand(_chapter.Id, _scene.Id, _writer, items), default);

        result.IsSuccess.Should().BeTrue();
        line1.LineOrder.Should().Be(2);
        line2.LineOrder.Should().Be(1);
    }
}
