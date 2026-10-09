using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using PanelForge.AI.Contracts;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.AiAgents.Commands.MarkStaleRuns;
using PanelForge.Application.Features.AiAgents.Queries.GetRun;
using PanelForge.Application.Features.AiAgents.Queries.GetSuggestions;
using PanelForge.Application.Features.Scenes.Commands.UpdateScene;
using PanelForge.Domain.Entities.Ai;
using PanelForge.Domain.Enums;
using PanelForge.Infrastructure.Persistence.Seeders;
using Xunit;

namespace PanelForge.UnitTests.Ai;

/// <summary>BR-07 (quyền), BR-03 (scope), BR-11 (version nguồn), BR-15 (Bible tại chương), BR-02 (AI chỉ đề xuất), chịu lỗi AI.</summary>
public class RunWorkflowCommandTests
{
    [Fact]
    public async Task Run_SavesQueuedRun_WithBibleSnapshotAtChapter_AndHandsJobToAiService()
    {
        var h = await AiHarness.CreateAsync();

        var result = await h.RunHandler().Handle(h.RunCommand(), default);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be(AiRunStatus.Queued);
        result.Value.Provider.Should().Be("Mock");
        var run = await h.RunAsync(result.Value.RunId);
        run.WorkspaceId.Should().Be(AiDemoSeeder.WorkspaceId);
        run.RequestedByUserId.Should().Be(AiHarness.Writer);
        run.BaseChapterVersion.Should().Be(0);

        // Snapshot: Bible hiệu lực + revision IDs đã dùng
        var ids = JsonSerializer.Deserialize<List<Guid>>(run.BibleRevisionIdsJson)!;
        ids.Should().HaveCount(5);
        ids.Should().BeEquivalentTo(await h.Db.BibleEntryRevisions.Select(r => r.Id).ToListAsync());
        run.ContextSnapshotJson.Should().Contain("chìa khóa bạc từ Chương 5");

        // Job giao cho AI service không mang API key khi chạy Mock
        h.LastJob!.RunId.Should().Be(run.Id);
        h.LastJob.ApiKey.Should().BeNull();
        h.LastJob.ToString().Should().NotContain("sk-");
    }

    [Fact]
    public async Task Run_ByNonMember_IsForbidden_AndCreatesNothing()
    {
        var h = await AiHarness.CreateAsync();

        var result = await h.RunHandler().Handle(h.RunCommand(user: AiHarness.Reader), default);

        result.ErrorCode.Should().Be(ResultErrorCodes.Forbidden);
        h.Db.AiWorkflowRuns.Count().Should().Be(0);
        h.AiClient.Verify(c => c.StartRunAsync(It.IsAny<AiRunJob>(), default), Times.Never);
    }

    [Fact]
    public async Task Run_ByArtistOrEditor_IsForbidden_OnlyProducerAndWriterMayRun()
    {
        var h = await AiHarness.CreateAsync();

        (await h.RunHandler().Handle(h.RunCommand(user: AiHarness.Artist), default)).ErrorCode.Should().Be(ResultErrorCodes.Forbidden);
        (await h.RunHandler().Handle(h.RunCommand(user: AiHarness.Editor), default)).ErrorCode.Should().Be(ResultErrorCodes.Forbidden);
        (await h.RunHandler().Handle(h.RunCommand(user: AiHarness.Producer), default)).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Run_WhenAiDisabledForWorkspace_ReturnsAiDisabled_EvenInMockMode()
    {
        var h = await AiHarness.CreateAsync();
        var config = await h.Db.WorkspaceAiConfigs.SingleAsync();
        config.UpdateSettings("Google Gemini", null, isEnabled: false, 1_000_000, "[\"gemini-2.5-flash\"]");
        await h.Db.SaveChangesAsync();

        var result = await h.RunHandler().Handle(h.RunCommand(), default);

        result.ErrorCode.Should().Be(ResultErrorCodes.AiDisabled);
        h.Db.AiWorkflowRuns.Count().Should().Be(0);
    }

    [Fact]
    public async Task Run_WithStaleChapterVersionOrScriptRevision_Conflicts()
    {
        var h = await AiHarness.CreateAsync();

        (await h.RunHandler().Handle(h.RunCommand(chapterVersion: 7), default)).ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        (await h.RunHandler().Handle(h.RunCommand(baseScriptRevision: Guid.NewGuid()), default)).ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        h.Db.AiWorkflowRuns.Count().Should().Be(0);
    }

    [Fact]
    public async Task Run_WithSceneFromAnotherChapter_IsRejected()
    {
        var h = await AiHarness.CreateAsync();
        var cmd = h.RunCommand() with { SceneId = Guid.NewGuid() };

        (await h.RunHandler().Handle(cmd, default)).ErrorCode.Should().Be(ResultErrorCodes.NotFound);
    }

    [Fact]
    public async Task Run_WhenAiServiceIsDown_ReturnsAiUnavailable_RunMarkedFailed_AndOtherBackendFeaturesStillWork()
    {
        var h = await AiHarness.CreateAsync();
        h.AiClient.Setup(c => c.StartRunAsync(It.IsAny<AiRunJob>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AiRunAck(false, ResultErrorCodes.AiUnavailable, "AI service đang tắt hoặc không phản hồi."));

        var result = await h.RunHandler().Handle(h.RunCommand(), default);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ResultErrorCodes.AiUnavailable);
        var run = await h.Db.AiWorkflowRuns.SingleAsync();
        run.Status.Should().Be(AiRunStatus.Failed);
        run.ErrorCode.Should().Be(ResultErrorCodes.AiUnavailable);

        // Chức năng Backend không phụ thuộc AI vẫn hoạt động bình thường
        var update = await new UpdateSceneCommandHandler(h.Db)
            .Handle(new UpdateSceneCommand(AiDemoSeeder.SceneId, AiHarness.Writer, "Heading mới", "Tóm tắt mới"), default);
        update.IsSuccess.Should().BeTrue();
        (await h.Db.Scenes.SingleAsync()).Heading.Should().Be("Heading mới");
    }

    [Fact]
    public async Task Run_GeminiMode_SendsDecryptedKeyOnlyInJob_AndChecksQuota()
    {
        var h = await AiHarness.CreateAsync();
        h.Settings.Provider = "Gemini";
        h.KeyProtector.Setup(k => k.Unprotect(It.IsAny<string>())).Returns("test-key-123");
        var config = await h.Db.WorkspaceAiConfigs.SingleAsync();
        config.UpdateSettings("Google Gemini", "ciphertext", true, 1_000_000, "[\"gemini-2.5-flash\"]");
        await h.Db.SaveChangesAsync();

        var result = await h.RunHandler().Handle(h.RunCommand(), default);

        result.IsSuccess.Should().BeTrue();
        h.LastJob!.ApiKey.Should().Be("test-key-123");
        h.LastJob.ToString().Should().NotContain("test-key-123");
        var run = await h.Db.AiWorkflowRuns.SingleAsync();
        run.ContextSnapshotJson.Should().NotContain("test-key-123");
        run.Provider.Should().Be("Gemini");
        h.Usage.Verify(u => u.CheckAvailabilityAsync(AiDemoSeeder.WorkspaceId, default), Times.Once);
    }

    [Fact]
    public async Task Run_GeminiMode_QuotaExceeded_IsBlocked_AndNotFallenBackToMock()
    {
        var h = await AiHarness.CreateAsync();
        h.Settings.Provider = "Gemini";
        h.Usage.Setup(u => u.CheckAvailabilityAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AiAvailability.Blocked(AiUsageStatus.QuotaExceeded, "Hết hạn mức."));

        var result = await h.RunHandler().Handle(h.RunCommand(), default);

        result.ErrorCode.Should().Be(ResultErrorCodes.AiQuotaExceeded);
        h.Db.AiWorkflowRuns.Count().Should().Be(0);
        h.AiClient.Verify(c => c.StartRunAsync(It.IsAny<AiRunJob>(), default), Times.Never);
    }

    [Fact]
    public async Task Run_GeminiMode_ModelNotAllowedForWorkspace_IsRejected()
    {
        var h = await AiHarness.CreateAsync();
        h.Settings.Provider = "Gemini";
        h.Settings.GeminiModel = "gemini-9-ultra";

        var result = await h.RunHandler().Handle(h.RunCommand(), default);

        result.ErrorCode.Should().Be(ResultErrorCodes.Validation);
        result.ErrorMessage.Should().Contain("gemini-9-ultra");
    }

    [Fact]
    public async Task GetRun_AndSuggestions_RequireMembership_AndScopeToSeries()
    {
        var h = await AiHarness.CreateAsync();
        var runId = await h.CompleteDemoRunAsync();
        var getRun = new GetRunQueryHandler(h.Db, h.Auth);
        var getSuggestions = new GetSuggestionsQueryHandler(h.Db, h.Auth);

        var ok = await getRun.Handle(new GetRunQuery(AiDemoSeeder.SeriesId, runId, AiHarness.Editor), default);
        ok.IsSuccess.Should().BeTrue();
        ok.Value!.Steps.Should().HaveCount(13, "6 step của agent chuyên môn + 7 quyết định điều phối của Supervisor");
        ok.Value.Suggestions.Should().HaveCount(2);

        (await getRun.Handle(new GetRunQuery(AiDemoSeeder.SeriesId, runId, AiHarness.Reader), default))
            .ErrorCode.Should().Be(ResultErrorCodes.Forbidden);
        (await getRun.Handle(new GetRunQuery(Guid.NewGuid(), runId, AiHarness.Editor), default))
            .ErrorCode.Should().Be(ResultErrorCodes.NotFound);
        (await getSuggestions.Handle(new GetSuggestionsQuery(AiDemoSeeder.SeriesId, AiHarness.Reader, null, null), default))
            .ErrorCode.Should().Be(ResultErrorCodes.Forbidden);

        var pending = await getSuggestions.Handle(
            new GetSuggestionsQuery(AiDemoSeeder.SeriesId, AiHarness.Writer, AiSuggestionStatus.Pending, runId), default);
        pending.Value.Should().HaveCount(2);
    }

    [Fact]
    public async Task MarkStaleRuns_InterruptsOnlyActiveRunsWithoutRecentActivity()
    {
        var h = await AiHarness.CreateAsync();
        var staleRun = await h.StartRunAsync();
        var freshRun = await h.StartRunAsync();
        var doneRun = await h.CompleteDemoRunAsync();

        // ép "quá hạn" bằng cách tạo run mới có LastActivity cũ: dùng reflection trên entity (không có setter công khai)
        typeof(AiWorkflowRun).GetProperty(nameof(AiWorkflowRun.LastActivityAtUtc))!
            .SetValue(await h.RunAsync(staleRun), DateTime.UtcNow.AddMinutes(-30));
        typeof(AiWorkflowRun).GetProperty(nameof(AiWorkflowRun.LastActivityAtUtc))!
            .SetValue(await h.RunAsync(doneRun), DateTime.UtcNow.AddMinutes(-30));
        await h.Db.SaveChangesAsync();

        var count = await new MarkStaleRunsCommandHandler(h.Db, h.Settings).Handle(new MarkStaleRunsCommand(), default);

        count.Should().Be(1);
        (await h.RunAsync(staleRun)).Status.Should().Be(AiRunStatus.Interrupted);
        (await h.RunAsync(staleRun)).ErrorCode.Should().Be("AI_INTERRUPTED");
        (await h.RunAsync(freshRun)).IsActive.Should().BeTrue();
        (await h.RunAsync(doneRun)).Status.Should().Be(AiRunStatus.Completed);
    }

    [Fact]
    public async Task DemoSeeder_IsIdempotent_AndDoesNotTouchExistingData()
    {
        var h = await AiHarness.CreateAsync();
        var chapter = await h.Db.Chapters.SingleAsync();
        chapter.UpdateDetails("Tên đã sửa bởi người dùng", null);
        await h.Db.SaveChangesAsync();
        var before = (h.Db.StudioWorkspaces.Count(), h.Db.BibleEntries.Count(), h.Db.BibleEntryRevisions.Count(), h.Db.Scenes.Count());

        var again = await AiDemoSeeder.SeedAsync(h.Db);

        again.Should().BeFalse();
        (h.Db.StudioWorkspaces.Count(), h.Db.BibleEntries.Count(), h.Db.BibleEntryRevisions.Count(), h.Db.Scenes.Count()).Should().Be(before);
        (await h.Db.Chapters.SingleAsync()).Title.Should().Be("Tên đã sửa bởi người dùng");
    }
}