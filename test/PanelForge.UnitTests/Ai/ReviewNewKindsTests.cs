using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PanelForge.AI.Contracts;
using PanelForge.Application.Common;
using PanelForge.Application.Features.AiAgents.Queries.GetRun;
using PanelForge.Domain.Entities.Ai;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;
using PanelForge.Infrastructure.Persistence.Seeders;
using Xunit;

namespace PanelForge.UnitTests.Ai;

/// <summary>
/// Accept/Reject/RequestChanges cho prompt ảnh (Artist) và rút gọn thoại (Letterer): quyền theo LOẠI đề xuất, append-only + provenance,
/// và Accept bị chặn khi stale version/revision, chương bị khoá, hoặc liên kết (panel/dòng thoại/script) không còn đúng.
/// Sau AcceptScriptAndLayoutAsync: Script revision v1, Layout revision v1, chapter version = 2.
/// </summary>
public class ReviewNewKindsTests
{
    private const long ReadyVersion = 2;

    private static async Task<(AiHarness H, AiSuggestion S)> ArtistSuggestionAsync()
    {
        var h = await AiHarness.CreateAsync();
        await h.AcceptScriptAndLayoutAsync();
        var runId = await h.RunMockAsync(AiWorkflowType.Artist);
        return (h, await h.SuggestionAsync(runId, AiSuggestionKind.ImagePrompt));
    }

    private static async Task<(AiHarness H, AiSuggestion S)> LetteringSuggestionAsync()
    {
        var h = await AiHarness.CreateAsync();
        await h.AcceptScriptAndLayoutAsync();
        var runId = await h.RunMockAsync(AiWorkflowType.Letterer);
        return (h, await h.SuggestionAsync(runId, AiSuggestionKind.Lettering));
    }

    private static async Task LockAsync(AiHarness h)
    {
        typeof(Chapter).GetProperty(nameof(Chapter.Status))!.SetValue(await h.Db.Chapters.SingleAsync(), ChapterStatus.Locked);
        await h.Db.SaveChangesAsync();
    }

    // ── Prompt ảnh ───────────────────────────────────────────────────────────
    [Fact]
    public async Task AcceptImagePrompt_WritesAnAppendOnlyRevision_WithMapping_AndTouchesNothingElse()
    {
        var (h, s) = await ArtistSuggestionAsync();
        var scriptBefore = (await h.Db.ScriptLines.ToListAsync()).Select(l => l.DialogueText).ToList();
        var panelsBefore = h.Db.Panels.Count();
        var bibleBefore = h.Db.BibleEntryRevisions.Count();

        var result = await h.ReviewAsync(s.Id, AiSuggestionStatus.Accepted, AiHarness.Artist, ReadyVersion);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value!.ChapterVersion.Should().Be(ReadyVersion + 1);
        var revision = await h.Db.ContentRevisions.SingleAsync(r => r.Kind == ContentRevisionKind.ImagePrompt);
        revision.VersionNumber.Should().Be(1);
        revision.SourceSuggestionId.Should().Be(s.Id);
        revision.PayloadJson.Should().Be(s.PayloadJson);
        revision.ContentHash.Should().Be(s.PayloadHash);
        revision.MappingJson.Should().Contain("layoutRevisionId").And.Contain("referenceIds");
        var decision = await h.Db.AiSuggestionDecisions.SingleAsync(d => d.SuggestionId == s.Id);
        (decision.Decision, decision.DecidedByUserId, decision.ResultRevisionId).Should().Be((AiSuggestionStatus.Accepted, AiHarness.Artist, (Guid?)revision.Id));
        h.Db.AuditLogs.Any(a => a.EntityName == "ContentRevision").Should().BeTrue();

        (await h.Db.ScriptLines.ToListAsync()).Select(l => l.DialogueText).Should().Equal(scriptBefore);
        h.Db.Panels.Count().Should().Be(panelsBefore);
        h.Db.BibleEntryRevisions.Count().Should().Be(bibleBefore);
        (await h.Db.Chapters.SingleAsync()).Status.Should().Be(ChapterStatus.InProduction, "Accept không Approve chapter hay chuyển stage");
    }

    [Theory]
    [InlineData("Writer", false)]
    [InlineData("Letterer", false)]
    [InlineData("Reader", false)]
    [InlineData("Artist", true)]
    [InlineData("Editor", true)]
    [InlineData("Producer", true)]
    public async Task ImagePrompt_DecisionRoles_FollowTheKind(string user, bool allowed)
    {
        var (h, s) = await ArtistSuggestionAsync();
        var id = user switch
        {
            "Writer" => AiHarness.Writer, "Letterer" => AiHarness.Letterer, "Reader" => AiHarness.Reader,
            "Artist" => AiHarness.Artist, "Editor" => AiHarness.Editor, _ => AiHarness.Producer
        };

        var result = await h.ReviewAsync(s.Id, AiSuggestionStatus.Accepted, id, ReadyVersion);

        if (allowed) result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        else
        {
            result.ErrorCode.Should().Be(ResultErrorCodes.Forbidden);
            h.Db.ContentRevisions.Count(r => r.Kind == ContentRevisionKind.ImagePrompt).Should().Be(0);
            (await h.Db.AiSuggestions.SingleAsync(x => x.Id == s.Id)).Status.Should().Be(AiSuggestionStatus.Pending);
        }
    }

    [Fact]
    public async Task ImagePrompt_RejectAndRequestChanges_RecordDecisionOnly_RequestChangesNeedsReason()
    {
        var (h, s) = await ArtistSuggestionAsync();

        (await h.ReviewAsync(s.Id, AiSuggestionStatus.ChangesRequested, AiHarness.Artist, null)).ErrorCode.Should().Be(ResultErrorCodes.Validation);
        (await h.ReviewAsync(s.Id, AiSuggestionStatus.Rejected, AiHarness.Artist, null, reason: "Không hợp phong cách")).IsSuccess.Should().BeTrue();

        (await h.Db.AiSuggestions.SingleAsync(x => x.Id == s.Id)).Status.Should().Be(AiSuggestionStatus.Rejected);
        h.Db.ContentRevisions.Count(r => r.Kind == ContentRevisionKind.ImagePrompt).Should().Be(0);
        (await h.Db.Chapters.SingleAsync()).VersionVector.Should().Be(ReadyVersion);
        (await h.ReviewAsync(s.Id, AiSuggestionStatus.Accepted, AiHarness.Artist, ReadyVersion)).ErrorCode.Should().Be(ResultErrorCodes.Conflict, "đã quyết định rồi");
    }

    [Fact]
    public async Task ImagePrompt_Accept_StaleChapterVersion_Conflicts_AndSavesNothing()
    {
        var (h, s) = await ArtistSuggestionAsync();

        (await h.ReviewAsync(s.Id, AiSuggestionStatus.Accepted, AiHarness.Artist, ReadyVersion - 1)).ErrorCode.Should().Be(ResultErrorCodes.Conflict);

        // Chương đã đổi sau khi AI chạy: client gửi version mới nhưng đề xuất dựa trên version cũ
        (await h.Db.Chapters.SingleAsync()).IncrementVersion();
        await h.Db.SaveChangesAsync();
        var moved = await h.ReviewAsync(s.Id, AiSuggestionStatus.Accepted, AiHarness.Artist, ReadyVersion + 1);
        moved.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        moved.ErrorMessage.Should().Contain("dựa trên");
        h.Db.ContentRevisions.Count(r => r.Kind == ContentRevisionKind.ImagePrompt).Should().Be(0);
        h.Db.AiSuggestionDecisions.Count(d => d.SuggestionId == s.Id).Should().Be(0);
    }

    [Fact]
    public async Task ImagePrompt_Accept_WhenLayoutHasNewerRevision_Conflicts()
    {
        var (h, s) = await ArtistSuggestionAsync();
        var latest = await h.Db.ContentRevisions.SingleAsync(r => r.Kind == ContentRevisionKind.Layout);
        h.Db.ContentRevisions.Add(ContentRevision.Create(latest.ChapterId, latest.SceneId, ContentRevisionKind.Layout, 2, "Layout mới",
            latest.PayloadJson, "hash2", latest.Id, null, latest.MappingJson));
        await h.Db.SaveChangesAsync();

        var result = await h.ReviewAsync(s.Id, AiSuggestionStatus.Accepted, AiHarness.Artist, ReadyVersion);

        result.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        result.ErrorMessage.Should().Contain("Layout");
        h.Db.ContentRevisions.Count(r => r.Kind == ContentRevisionKind.ImagePrompt).Should().Be(0);
    }

    [Fact]
    public async Task ImagePrompt_Accept_WhenAPanelNoLongerExists_Conflicts_WrongLink()
    {
        var (h, s) = await ArtistSuggestionAsync();
        h.Db.Panels.Remove(await h.Db.Panels.FirstAsync());
        await h.Db.SaveChangesAsync();

        var result = await h.ReviewAsync(s.Id, AiSuggestionStatus.Accepted, AiHarness.Artist, ReadyVersion);

        result.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        result.ErrorMessage.Should().Contain("liên kết sai");
        h.Db.ContentRevisions.Count(r => r.Kind == ContentRevisionKind.ImagePrompt).Should().Be(0);
    }

    [Fact]
    public async Task ImagePrompt_Accept_OnLockedChapter_IsBlocked()
    {
        var (h, s) = await ArtistSuggestionAsync();
        await LockAsync(h);

        var result = await h.ReviewAsync(s.Id, AiSuggestionStatus.Accepted, AiHarness.Artist, ReadyVersion);

        result.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        result.ErrorMessage.Should().Contain("Locked");
        h.Db.ContentRevisions.Count(r => r.Kind == ContentRevisionKind.ImagePrompt).Should().Be(0);
    }

    [Fact]
    public async Task ImagePrompt_Accept_WhenPayloadNoLongerValid_SavesNothing()
    {
        var (h, s) = await ArtistSuggestionAsync();
        var set = AiJson.Deserialize<ArtistPromptSet>(s.PayloadJson)!;
        var tampered = AiJson.Serialize(set with { Panels = [set.Panels[0] with { ReferenceIds = [Guid.NewGuid()] }] });
        typeof(AiSuggestion).GetProperty(nameof(AiSuggestion.PayloadJson))!.SetValue(s, tampered);
        await h.Db.SaveChangesAsync();

        var result = await h.ReviewAsync(s.Id, AiSuggestionStatus.Accepted, AiHarness.Artist, ReadyVersion);

        result.ErrorCode.Should().Be(ResultErrorCodes.Validation);
        h.Db.ContentRevisions.Count(r => r.Kind == ContentRevisionKind.ImagePrompt).Should().Be(0);
    }

    // ── Rút gọn thoại ────────────────────────────────────────────────────────
    [Fact]
    public async Task AcceptLettering_CreatesANewScriptRevision_KeepsTheOriginalInTheOldOne_AndRecordsProvenance()
    {
        var (h, s) = await LetteringSuggestionAsync();
        var set = AiJson.Deserialize<LetteringSuggestionSet>(s.PayloadJson)!;
        var previous = await h.Db.ContentRevisions.SingleAsync(r => r.Kind == ContentRevisionKind.Script);

        var result = await h.ReviewAsync(s.Id, AiSuggestionStatus.Accepted, AiHarness.Letterer, ReadyVersion);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value!.ChapterVersion.Should().Be(ReadyVersion + 1);
        var lines = await h.Db.ScriptLines.ToListAsync();
        foreach (var sug in set.Suggestions)
            lines.Single(l => l.Id == sug.SourceScriptLineId).DialogueText.Should().Be(sug.SuggestedText);

        var revisions = await h.Db.ContentRevisions.Where(r => r.Kind == ContentRevisionKind.Script).OrderBy(r => r.VersionNumber).ToListAsync();
        revisions.Should().HaveCount(2, "append-only: revision cũ giữ nguyên, revision mới nối sau");
        var created = revisions[1];
        (created.VersionNumber, created.PreviousRevisionId, created.SourceSuggestionId).Should().Be((2, (Guid?)previous.Id, (Guid?)s.Id));
        created.MappingJson.Should().Contain("Lettering");
        foreach (var sug in set.Suggestions)
        {
            revisions[0].PayloadJson.Should().Contain(sug.OriginalText, "thoại gốc còn nguyên trong revision cũ — không bị ghi đè âm thầm");
            created.PayloadJson.Should().Contain(sug.SuggestedText);
        }
        (await h.Db.AiSuggestionDecisions.SingleAsync(d => d.SuggestionId == s.Id)).ResultRevisionId.Should().Be(created.Id);
    }

    [Theory]
    [InlineData("Letterer", true)]
    [InlineData("Writer", true)]
    [InlineData("Editor", true)]
    [InlineData("Artist", false)]
    [InlineData("Reader", false)]
    public async Task Lettering_DecisionRoles_FollowTheKind(string user, bool allowed)
    {
        var (h, s) = await LetteringSuggestionAsync();
        var id = user switch
        {
            "Writer" => AiHarness.Writer, "Letterer" => AiHarness.Letterer, "Reader" => AiHarness.Reader,
            "Artist" => AiHarness.Artist, _ => AiHarness.Editor
        };

        var result = await h.ReviewAsync(s.Id, AiSuggestionStatus.Accepted, id, ReadyVersion);

        result.IsSuccess.Should().Be(allowed, result.ErrorMessage);
        if (!allowed) result.ErrorCode.Should().Be(ResultErrorCodes.Forbidden);
    }

    [Fact]
    public async Task Lettering_Accept_WhenScriptHasANewerRevision_Conflicts_AndKeepsLines()
    {
        var (h, s) = await LetteringSuggestionAsync();
        var current = await h.Db.ContentRevisions.SingleAsync(r => r.Kind == ContentRevisionKind.Script);
        h.Db.ContentRevisions.Add(ContentRevision.Create(current.ChapterId, current.SceneId, ContentRevisionKind.Script, 2, "Sửa tay",
            current.PayloadJson, "hash-manual", current.Id, null));
        await h.Db.SaveChangesAsync();
        var before = (await h.Db.ScriptLines.ToListAsync()).Select(l => l.DialogueText).ToList();

        var result = await h.ReviewAsync(s.Id, AiSuggestionStatus.Accepted, AiHarness.Letterer, ReadyVersion);

        result.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        result.ErrorMessage.Should().Contain("revision mới hơn");
        (await h.Db.ScriptLines.ToListAsync()).Select(l => l.DialogueText).Should().Equal(before);
    }

    [Fact]
    public async Task Lettering_Accept_WhenALineWasEditedSinceTheSuggestion_Conflicts_WrongLink()
    {
        var (h, s) = await LetteringSuggestionAsync();
        var set = AiJson.Deserialize<LetteringSuggestionSet>(s.PayloadJson)!;
        var line = await h.Db.ScriptLines.SingleAsync(l => l.Id == set.Suggestions[0].SourceScriptLineId);
        line.UpdateContent("Thoại đã bị người khác sửa tay", line.StageDirection, line.SpeakerCharacterId);
        await h.Db.SaveChangesAsync();

        var result = await h.ReviewAsync(s.Id, AiSuggestionStatus.Accepted, AiHarness.Letterer, ReadyVersion);

        result.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        result.ErrorMessage.Should().Contain("liên kết sai");
        (await h.Db.ScriptLines.SingleAsync(l => l.Id == line.Id)).DialogueText.Should().Be("Thoại đã bị người khác sửa tay");
        h.Db.ContentRevisions.Count(r => r.Kind == ContentRevisionKind.Script).Should().Be(1);
    }

    [Fact]
    public async Task Lettering_Accept_WhenLayoutChanged_OrChapterLocked_OrVersionStale_IsBlocked()
    {
        var (h, s) = await LetteringSuggestionAsync();
        (await h.ReviewAsync(s.Id, AiSuggestionStatus.Accepted, AiHarness.Letterer, ReadyVersion - 1)).ErrorCode.Should().Be(ResultErrorCodes.Conflict);

        var layout = await h.Db.ContentRevisions.SingleAsync(r => r.Kind == ContentRevisionKind.Layout);
        h.Db.ContentRevisions.Add(ContentRevision.Create(layout.ChapterId, layout.SceneId, ContentRevisionKind.Layout, 2, "Layout mới",
            layout.PayloadJson, "hash2", layout.Id, null, layout.MappingJson));
        await h.Db.SaveChangesAsync();
        var changed = await h.ReviewAsync(s.Id, AiSuggestionStatus.Accepted, AiHarness.Letterer, ReadyVersion);
        changed.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        changed.ErrorMessage.Should().Contain("Layout");

        await LockAsync(h);
        (await h.ReviewAsync(s.Id, AiSuggestionStatus.Accepted, AiHarness.Letterer, ReadyVersion)).ErrorMessage.Should().Contain("Locked");
        h.Db.ContentRevisions.Count(r => r.Kind == ContentRevisionKind.Script).Should().Be(1);
    }

    // ── Layout từ workflow Storyboard (không có Script cha) ──────────────────
    [Fact]
    public async Task StoryboardLayout_Accept_WhenTheScriptChangedAfterTheRun_Conflicts()
    {
        var h = await AiHarness.CreateAsync();
        var scriptRun = await h.RunMockAsync(AiWorkflowType.Script);
        var script = await h.SuggestionAsync(scriptRun, AiSuggestionKind.Script);
        (await h.ReviewAsync(script.Id, AiSuggestionStatus.Accepted, AiHarness.Writer, 0)).IsSuccess.Should().BeTrue();
        var runId = await h.RunMockAsync(AiWorkflowType.Storyboard);
        var layout = await h.SuggestionAsync(runId, AiSuggestionKind.Layout);
        var current = await h.Db.ContentRevisions.SingleAsync();
        h.Db.ContentRevisions.Add(ContentRevision.Create(current.ChapterId, current.SceneId, ContentRevisionKind.Script, 2, "Sửa tay",
            current.PayloadJson, "hash-manual", current.Id, null));
        await h.Db.SaveChangesAsync();

        var result = await h.ReviewAsync(layout.Id, AiSuggestionStatus.Accepted, AiHarness.Writer, 1);

        result.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        result.ErrorMessage.Should().Contain("revision mới hơn");
        h.Db.Pages.Count().Should().Be(0);
    }

    // ── CanDecide / quyền hiển thị nút ───────────────────────────────────────
    [Fact]
    public async Task RunDto_TellsWhoCanDecide_PerSuggestionKind_AndStopsAfterDecision()
    {
        var (h, s) = await ArtistSuggestionAsync();
        var get = new GetRunQueryHandler(h.Db, h.Auth);
        async Task<PanelForge.Application.Features.AiAgents.Models.AiSuggestionDto> As(Guid user) =>
            (await get.Handle(new GetRunQuery(AiDemoSeeder.SeriesId, s.RunId, user), default)).Value!.Suggestions.Single();

        var asArtist = await As(AiHarness.Artist);
        asArtist.Kind.Should().Be(AiSuggestionKind.ImagePrompt);
        asArtist.CanDecide.Should().BeTrue();
        asArtist.DecisionRoles.Should().BeEquivalentTo([WorkspaceRole.Producer, WorkspaceRole.Artist, WorkspaceRole.Editor]);
        (await As(AiHarness.Editor)).CanDecide.Should().BeTrue();
        (await As(AiHarness.Writer)).CanDecide.Should().BeFalse();
        (await As(AiHarness.Letterer)).CanDecide.Should().BeFalse();
        (await get.Handle(new GetRunQuery(AiDemoSeeder.SeriesId, s.RunId, AiHarness.Reader), default)).ErrorCode.Should().Be(ResultErrorCodes.Forbidden);

        (await h.ReviewAsync(s.Id, AiSuggestionStatus.Rejected, AiHarness.Artist, null, reason: "Không dùng")).IsSuccess.Should().BeTrue();
        (await As(AiHarness.Artist)).CanDecide.Should().BeFalse("đã quyết định xong");
    }

    [Fact]
    public async Task ReportOnlyRuns_ProduceNoSuggestion_SoThereIsNothingToAcceptOrReject()
    {
        var h = await AiHarness.CreateAsync();
        await h.AcceptScriptAndLayoutAsync();
        var editor = await h.RunMockAsync(AiWorkflowType.Editor);
        var producer = await h.RunMockAsync(AiWorkflowType.Producer);

        foreach (var runId in new[] { editor, producer })
        {
            var dto = (await new GetRunQueryHandler(h.Db, h.Auth).Handle(new GetRunQuery(AiDemoSeeder.SeriesId, runId, AiHarness.Producer), default)).Value!;
            dto.Status.Should().Be(AiRunStatus.Completed);
            dto.Suggestions.Should().BeEmpty();
        }
        h.Db.AiSuggestions.Count(x => x.Status == AiSuggestionStatus.Pending).Should().Be(0);
    }
}
