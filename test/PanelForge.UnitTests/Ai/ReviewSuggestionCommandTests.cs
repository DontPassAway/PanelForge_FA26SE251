using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using PanelForge.AI.Contracts;
using PanelForge.Application.Common;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;
using PanelForge.Infrastructure.Persistence.Seeders;
using Xunit;

namespace PanelForge.UnitTests.Ai;

/// <summary>BR-02 (người quyết định), BR-10 (provenance), BR-11 (409), BR-12 (Locked), BR-01 (revision append-only), BR-18 (audit).</summary>
public class ReviewSuggestionCommandTests
{
    [Fact]
    public async Task AcceptScript_CreatesRevisionDecisionAndLines_Atomically_WithProvenance()
    {
        var h = await AiHarness.CreateAsync();
        var runId = await h.CompleteDemoRunAsync();
        var script = await h.SuggestionAsync(runId, AiSuggestionKind.Script);
        var draft = AiJson.Deserialize<ScriptDraft>(script.PayloadJson)!;

        var result = await h.ReviewAsync(script.Id, AiSuggestionStatus.Accepted, AiHarness.Writer, chapterVersion: 0, reason: "Đúng ý");

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value!.ChapterVersion.Should().Be(1);

        // Nội dung thật được áp dụng, dòng mang đúng LineId do ứng dụng cấp
        var lines = await h.Db.ScriptLines.Where(l => l.SceneId == AiDemoSeeder.SceneId).OrderBy(l => l.LineOrder).ToListAsync();
        lines.Select(l => l.Id).Should().Equal(draft.Lines.OrderBy(l => l.Order).Select(l => l.LineId));

        // Content revision append-only + liên kết nguồn AI
        var revision = await h.Db.ContentRevisions.SingleAsync();
        revision.Kind.Should().Be(ContentRevisionKind.Script);
        revision.VersionNumber.Should().Be(1);
        revision.SourceSuggestionId.Should().Be(script.Id);
        revision.PayloadJson.Should().Be(script.PayloadJson, "payload sinh ra bất biến, áp dụng nguyên văn");
        revision.ContentHash.Should().Be(script.PayloadHash);

        // Decision + provenance
        var decision = await h.Db.AiSuggestionDecisions.SingleAsync();
        decision.Decision.Should().Be(AiSuggestionStatus.Accepted);
        decision.DecidedByUserId.Should().Be(AiHarness.Writer);
        decision.ResultRevisionId.Should().Be(revision.Id);
        decision.ChapterVersionAtDecision.Should().Be(0);
        (await h.Db.AiSuggestions.SingleAsync(s => s.Id == script.Id)).Status.Should().Be(AiSuggestionStatus.Accepted);

        // Chương chưa bị Approve / chuyển stage; version tăng
        var chapter = await h.Db.Chapters.SingleAsync();
        chapter.Status.Should().Be(ChapterStatus.InProduction);
        chapter.VersionVector.Should().Be(1);
        // BR-18: thay đổi được ghi audit cùng lần lưu
        h.Db.AuditLogs.Any(a => a.EntityName == "ContentRevision").Should().BeTrue();
        h.Db.AuditLogs.Any(a => a.EntityName == "AiSuggestionDecision").Should().BeTrue();
    }

    [Fact]
    public async Task AcceptScript_WhenPayloadFailsRevalidation_SavesNothing()
    {
        var h = await AiHarness.CreateAsync();
        var runId = await h.CompleteDemoRunAsync();
        var script = await h.SuggestionAsync(runId, AiSuggestionKind.Script);
        // làm hỏng payload đã lưu: speaker không thuộc Bible
        typeof(PanelForge.Domain.Entities.Ai.AiSuggestion).GetProperty("PayloadJson")!
            .SetValue(script, script.PayloadJson.Replace(AiJson.Deserialize<ScriptDraft>(script.PayloadJson)!.Lines[1].SpeakerId!.Value.ToString(), Guid.NewGuid().ToString()));
        await h.Db.SaveChangesAsync();
        var auditBefore = h.Db.AuditLogs.Count();

        var result = await h.ReviewAsync(script.Id, AiSuggestionStatus.Accepted);

        result.ErrorCode.Should().Be(ResultErrorCodes.Validation);
        h.Db.ContentRevisions.Count().Should().Be(0);
        h.Db.AiSuggestionDecisions.Count().Should().Be(0);
        h.Db.ScriptLines.Count().Should().Be(0);
        (await h.Db.AiSuggestions.SingleAsync(s => s.Id == script.Id)).Status.Should().Be(AiSuggestionStatus.Pending);
        (await h.Db.Chapters.SingleAsync()).VersionVector.Should().Be(0);
        h.Db.AuditLogs.Count().Should().Be(auditBefore);
    }

    [Fact]
    public async Task Accept_OnLockedOrApprovedChapter_IsBlocked()
    {
        var h = await AiHarness.CreateAsync();
        var runId = await h.CompleteDemoRunAsync();
        var script = await h.SuggestionAsync(runId, AiSuggestionKind.Script);
        var chapter = await h.Db.Chapters.SingleAsync();
        typeof(Chapter).GetProperty(nameof(Chapter.Status))!.SetValue(chapter, ChapterStatus.Locked);
        await h.Db.SaveChangesAsync();

        var result = await h.ReviewAsync(script.Id, AiSuggestionStatus.Accepted);

        result.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        result.ErrorMessage.Should().Contain("Locked");
        h.Db.ContentRevisions.Count().Should().Be(0);
        h.Db.ScriptLines.Count().Should().Be(0);
    }

    [Fact]
    public async Task Accept_WithStaleVersion_Conflicts_BothWhenClientIsStaleAndWhenChapterMovedOn()
    {
        var h = await AiHarness.CreateAsync();
        var runId = await h.CompleteDemoRunAsync();
        var script = await h.SuggestionAsync(runId, AiSuggestionKind.Script);

        // client gửi version cũ
        (await h.ReviewAsync(script.Id, AiSuggestionStatus.Accepted, chapterVersion: 5)).ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        (await h.ReviewAsync(script.Id, AiSuggestionStatus.Accepted, chapterVersion: null)).ErrorCode.Should().Be(ResultErrorCodes.Validation);

        // người khác đã sửa Chapter sau khi AI chạy
        (await h.Db.Chapters.SingleAsync()).IncrementVersion();
        await h.Db.SaveChangesAsync();
        var result = await h.ReviewAsync(script.Id, AiSuggestionStatus.Accepted, chapterVersion: 1);

        result.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        result.ErrorMessage.Should().Contain("Version cũ");
        h.Db.ContentRevisions.Count().Should().Be(0);
        (await h.Db.AiSuggestions.SingleAsync(s => s.Id == script.Id)).Status.Should().Be(AiSuggestionStatus.Pending);
    }

    [Fact]
    public async Task Accept_WhenScriptGotNewRevisionSinceRun_Conflicts()
    {
        var h = await AiHarness.CreateAsync();
        var runId = await h.CompleteDemoRunAsync();
        var script = await h.SuggestionAsync(runId, AiSuggestionKind.Script);
        h.Db.ContentRevisions.Add(ContentRevision.Create(
            AiDemoSeeder.ChapterId, AiDemoSeeder.SceneId, ContentRevisionKind.Script, 1, "người sửa tay", "{}", "h", null, null));
        await h.Db.SaveChangesAsync();

        var result = await h.ReviewAsync(script.Id, AiSuggestionStatus.Accepted);

        result.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
    }

    [Fact]
    public async Task AcceptLayout_RequiresScriptFirst_ThenCreatesPagesPanelsAndRevisionWithMapping()
    {
        var h = await AiHarness.CreateAsync();
        var runId = await h.CompleteDemoRunAsync();
        var script = await h.SuggestionAsync(runId, AiSuggestionKind.Script);
        var layoutSuggestion = await h.SuggestionAsync(runId, AiSuggestionKind.Layout);

        (await h.ReviewAsync(layoutSuggestion.Id, AiSuggestionStatus.Accepted)).ErrorCode.Should().Be(ResultErrorCodes.Conflict);

        (await h.ReviewAsync(script.Id, AiSuggestionStatus.Accepted, chapterVersion: 0)).IsSuccess.Should().BeTrue();
        // sau khi Accept script, version là 1 → layout nền là 1
        (await h.ReviewAsync(layoutSuggestion.Id, AiSuggestionStatus.Accepted, chapterVersion: 0)).ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        var result = await h.ReviewAsync(layoutSuggestion.Id, AiSuggestionStatus.Accepted, chapterVersion: 1);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        var layout = AiJson.Deserialize<StoryboardLayout>(layoutSuggestion.PayloadJson)!;
        var pages = await h.Db.Pages.Where(p => p.ChapterId == AiDemoSeeder.ChapterId).ToListAsync();
        pages.Should().HaveCount(layout.Pages.Count);
        var panels = await h.Db.Panels.ToListAsync();
        panels.Should().HaveCount(layout.Pages.Sum(p => p.Panels.Count));
        // V1.2: Scene không chứa Page; Panel mang SceneId và ScriptLine được neo vào đúng một Panel
        panels.Should().OnlyContain(p => p.SceneId == AiDemoSeeder.SceneId);
        var anchored = await h.Db.ScriptLines.Where(l => l.PanelId != null).ToListAsync();
        anchored.Should().NotBeEmpty();
        anchored.Should().OnlyContain(l => panels.Any(p => p.Id == l.PanelId));
        panels.Select(p => p.BoundingBox).Should().OnlyContain(b => b.Contains("page-normalized") && b.Contains("\"width\"") && b.Contains("\"normalized\""));
        h.Pages.Verify(p => p.SaveAsync(It.IsAny<Page>(), default), Times.Exactly(layout.Pages.Count));

        var revision = await h.Db.ContentRevisions.SingleAsync(r => r.Kind == ContentRevisionKind.Layout);
        revision.SourceSuggestionId.Should().Be(layoutSuggestion.Id);
        revision.MappingJson.Should().Contain(panels[0].Id.ToString());
        var decision = await h.Db.AiSuggestionDecisions.SingleAsync(d => d.SuggestionId == layoutSuggestion.Id);
        decision.ResultRevisionId.Should().Be(revision.Id);
        (await h.Db.Chapters.SingleAsync()).VersionVector.Should().Be(2);
        (await h.Db.Chapters.SingleAsync()).Status.Should().Be(ChapterStatus.InProduction, "Accept không Approve Chapter");
    }

    [Fact]
    public async Task Accept_WhenBlockingFindingsRemain_IsRefused_ButRejectAndRequestChangesWork()
    {
        var h = await AiHarness.CreateAsync();
        var runId = await h.StartRunAsync($"Viết cảnh. {PanelForge.AI.Demo.Fixtures.MockFixtures.StubbornMarker}");
        await h.ExecuteWorkflowAsync(runId);
        var script = await h.SuggestionAsync(runId, AiSuggestionKind.Script);

        (await h.ReviewAsync(script.Id, AiSuggestionStatus.Accepted)).ErrorCode.Should().Be(ResultErrorCodes.Validation);
        (await h.ReviewAsync(script.Id, AiSuggestionStatus.ChangesRequested, reason: null)).ErrorCode.Should().Be(ResultErrorCodes.Validation);

        var result = await h.ReviewAsync(script.Id, AiSuggestionStatus.ChangesRequested, AiHarness.Editor, reason: "Còn mâu thuẫn chìa khóa bạc, viết lại.");
        result.IsSuccess.Should().BeTrue();
        (await h.Db.AiSuggestions.SingleAsync()).Status.Should().Be(AiSuggestionStatus.ChangesRequested);
        var decision = await h.Db.AiSuggestionDecisions.SingleAsync();
        decision.ResultRevisionId.Should().BeNull();
        h.Db.ContentRevisions.Count().Should().Be(0);
        h.Db.ScriptLines.Count().Should().Be(0);
    }

    [Fact]
    public async Task Reject_RecordsDecision_AppliesNothing_AndCannotBeDecidedTwice()
    {
        var h = await AiHarness.CreateAsync();
        var runId = await h.CompleteDemoRunAsync();
        var script = await h.SuggestionAsync(runId, AiSuggestionKind.Script);

        var result = await h.ReviewAsync(script.Id, AiSuggestionStatus.Rejected, AiHarness.Producer, reason: "Không hợp giọng truyện");

        result.IsSuccess.Should().BeTrue();
        (await h.Db.AiSuggestions.SingleAsync(s => s.Id == script.Id)).Status.Should().Be(AiSuggestionStatus.Rejected);
        h.Db.ContentRevisions.Count().Should().Be(0);
        h.Db.ScriptLines.Count().Should().Be(0);
        (await h.Db.Chapters.SingleAsync()).VersionVector.Should().Be(0);
        (await h.ReviewAsync(script.Id, AiSuggestionStatus.Accepted)).ErrorCode.Should().Be(ResultErrorCodes.Conflict);
    }

    [Theory]
    [InlineData("reader")]   // không phải thành viên
    [InlineData("artist")]   // thành viên nhưng không thuộc role được duyệt
    public async Task Review_ByUnauthorizedUser_IsForbidden(string who)
    {
        var h = await AiHarness.CreateAsync();
        var runId = await h.CompleteDemoRunAsync();
        var script = await h.SuggestionAsync(runId, AiSuggestionKind.Script);
        var user = who == "reader" ? AiHarness.Reader : AiHarness.Artist;

        foreach (var d in new[] { AiSuggestionStatus.Accepted, AiSuggestionStatus.Rejected, AiSuggestionStatus.ChangesRequested })
            (await h.ReviewAsync(script.Id, d, user, reason: "x")).ErrorCode.Should().Be(ResultErrorCodes.Forbidden);

        (await h.Db.AiSuggestions.SingleAsync(s => s.Id == script.Id)).Status.Should().Be(AiSuggestionStatus.Pending);
        h.Db.AiSuggestionDecisions.Count().Should().Be(0);
    }

    [Fact]
    public async Task Review_WithSuggestionFromAnotherSeries_IsNotFound()
    {
        var h = await AiHarness.CreateAsync();
        var runId = await h.CompleteDemoRunAsync();
        var script = await h.SuggestionAsync(runId, AiSuggestionKind.Script);

        var result = await h.ReviewHandler().Handle(new PanelForge.Application.Features.AiAgents.Commands.ReviewSuggestion.ReviewSuggestionCommand(
            Guid.NewGuid(), script.Id, AiHarness.Writer, AiSuggestionStatus.Rejected, null, null), default);

        result.ErrorCode.Should().Be(ResultErrorCodes.NotFound);
    }

    [Fact]
    public async Task SceneRevisions_ExposeProvenanceToMembersOnly_AndFeedTheNextRunBaseVersion()
    {
        var h = await AiHarness.CreateAsync();
        var runId = await h.CompleteDemoRunAsync();
        var script = await h.SuggestionAsync(runId, AiSuggestionKind.Script);
        var accepted = await h.ReviewAsync(script.Id, AiSuggestionStatus.Accepted);
        var handler = new PanelForge.Application.Features.AiAgents.Queries.GetContentRevisions.GetContentRevisionsQueryHandler(h.Db, h.Auth);

        var ok = await handler.Handle(new(AiDemoSeeder.SeriesId, AiDemoSeeder.SceneId, AiHarness.Editor), default);

        ok.Value.Should().ContainSingle(r => r.Id == accepted.Value!.ResultRevisionId && r.SourceSuggestionId == script.Id);
        (await handler.Handle(new(AiDemoSeeder.SeriesId, AiDemoSeeder.SceneId, AiHarness.Reader), default)).ErrorCode.Should().Be(ResultErrorCodes.Forbidden);
        (await handler.Handle(new(AiDemoSeeder.SeriesId, Guid.NewGuid(), AiHarness.Editor), default)).ErrorCode.Should().Be(ResultErrorCodes.NotFound);

        // Chạy lại workflow: phải gửi đúng chapterVersion mới + revision script mới nhất
        (await h.RunHandler().Handle(h.RunCommand(chapterVersion: 0), default)).ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        (await h.RunHandler().Handle(h.RunCommand(chapterVersion: 1, baseScriptRevision: accepted.Value!.ResultRevisionId), default))
            .IsSuccess.Should().BeTrue();
    }
}