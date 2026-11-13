using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PanelForge.AI.Contracts;
using PanelForge.Application.Common;
using PanelForge.Application.Features.AiAgents.Common;
using PanelForge.Application.Features.AiAgents.Queries.GetRun;
using PanelForge.Domain.Entities.Ai;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;
using PanelForge.Infrastructure.Persistence.Seeders;
using Xunit;

namespace PanelForge.UnitTests.Ai;

/// <summary>
/// Accept/Reject/RequestChanges cho prompt ảnh (Artist): quyền theo LOẠI đề xuất, append-only + provenance,
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
        (await h.Db.Chapters.SingleAsync()).Status.Should().Be(ChapterStatus.PreProduction, "Accept không Approve chapter hay chuyển stage");
    }

    [Fact]
    public async Task ImagePrompt_ForOnePanel_OnlyRewritesThatPanel_AndKeepsOtherApprovedPrompts()
    {
        var (h, full) = await ArtistSuggestionAsync();
        (await h.ReviewAsync(full.Id, AiSuggestionStatus.Accepted, AiHarness.Artist, ReadyVersion)).IsSuccess.Should().BeTrue();
        var before = AiJson.Deserialize<ArtistPromptSet>(full.PayloadJson)!;
        before.Panels.Should().HaveCountGreaterThan(1);
        var target = before.Panels[0].PanelId;

        var cmd = (await h.CommandAsync(AiWorkflowType.Artist)) with { PanelIds = [target] };
        var start = await h.RunHandler().Handle(cmd, default);
        start.IsSuccess.Should().BeTrue(start.ErrorMessage);
        await h.ExecuteWorkflowAsync(start.Value!.RunId);
        var partial = await h.SuggestionAsync(start.Value.RunId, AiSuggestionKind.ImagePrompt);
        AiJson.Deserialize<ArtistPromptSet>(partial.PayloadJson)!.Panels.Select(p => p.PanelId).Should().Equal(target);

        var result = await h.ReviewAsync(partial.Id, AiSuggestionStatus.Accepted, AiHarness.Artist, ReadyVersion + 1);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        var latest = await h.Db.ContentRevisions.Where(r => r.Kind == ContentRevisionKind.ImagePrompt)
            .OrderByDescending(r => r.VersionNumber).FirstAsync();
        latest.VersionNumber.Should().Be(2);
        var merged = AiJson.Deserialize<ArtistPromptSet>(latest.PayloadJson)!;
        merged.Panels.Select(p => p.PanelId).Should().BeEquivalentTo(before.Panels.Select(p => p.PanelId));
        merged.Panels.Skip(1).Select(p => p.Prompt).Should().Equal(before.Panels.Skip(1).Select(p => p.Prompt));
        latest.ContentHash.Should().Be(AiContentValidator.Sha256(latest.PayloadJson));
    }

    [Fact]
    public async Task RunWorkflow_PanelIds_RejectedOutsideArtist_OrWhenPanelNotInLayout()
    {
        var h = await AiHarness.CreateAsync();
        await h.AcceptScriptAndLayoutAsync();

        var notArtist = (await h.CommandAsync(AiWorkflowType.Storyboard)) with { PanelIds = [Guid.NewGuid()] };
        (await h.RunHandler().Handle(notArtist, default)).ErrorCode.Should().Be(ResultErrorCodes.Validation);

        var unknown = (await h.CommandAsync(AiWorkflowType.Artist)) with { PanelIds = [Guid.NewGuid()] };
        (await h.RunHandler().Handle(unknown, default)).ErrorCode.Should().Be(ResultErrorCodes.Validation);
        h.Db.AiWorkflowRuns.Count(r => r.WorkflowType == AiWorkflowType.Artist).Should().Be(0);
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

        foreach (var runId in new[] { editor })
        {
            var dto = (await new GetRunQueryHandler(h.Db, h.Auth).Handle(new GetRunQuery(AiDemoSeeder.SeriesId, runId, AiHarness.Producer), default)).Value!;
            dto.Status.Should().Be(AiRunStatus.Completed);
            dto.Suggestions.Should().BeEmpty();
        }
        h.Db.AiSuggestions.Count(x => x.Status == AiSuggestionStatus.Pending).Should().Be(0);
    }
}
