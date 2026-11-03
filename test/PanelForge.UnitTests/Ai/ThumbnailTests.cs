using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using PanelForge.AI.Contracts;
using PanelForge.AI.Gateway;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.AiAgents.Commands.RunThumbnail;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Enums;
using PanelForge.Infrastructure.Persistence.Seeders;
using Xunit;

namespace PanelForge.UnitTests.Ai;

/// <summary>Artist Thumbnail: guard BR-07/BR-03/BR-12/assignment/quota trước khi gọi AI; ảnh là AiSuggestion (BR-02); Accept thêm phiên bản artwork (BR-01).</summary>
public sealed class ThumbnailTests
{
    private sealed record Ctx(AiHarness H, Mock<IAssetStorage> Storage, Panel Panel, Guid StageId);

    /// <summary>Script + layout + prompt ảnh đã Accept; Artist đang giữ việc InProgress cho trang của panel đầu.</summary>
    private static async Task<Ctx> SetupAsync(bool assign = true)
    {
        var h = await AiHarness.CreateAsync();
        await h.AcceptScriptAndLayoutAsync();
        var runId = await h.RunMockAsync(AiWorkflowType.Artist);
        var prompt = await h.SuggestionAsync(runId, AiSuggestionKind.ImagePrompt);
        (await h.ReviewAsync(prompt.Id, AiSuggestionStatus.Accepted, AiHarness.Artist, chapterVersion: 2)).IsSuccess.Should().BeTrue();

        h.AiClient.Setup(c => c.GenerateImageAsync(It.IsAny<ImageGenRequest>(), It.IsAny<CancellationToken>()))
            .Returns((ImageGenRequest r, CancellationToken ct) => new MockImageGenerator().GenerateAsync(r, ct));
        var panel = await h.Db.Panels.OrderBy(p => p.ReadingOrder).FirstAsync();
        var stageId = Guid.NewGuid();
        if (assign)
        {
            var a = Assignment.Create(AiDemoSeeder.SeriesId, AiDemoSeeder.ChapterId, WorkflowEntityType.Page, panel.PageId,
                stageId, AiHarness.Artist, WorkspaceRole.Artist, "Vẽ trang");
            a.StartWork();
            h.Db.Assignments.Add(a);
            await h.Db.SaveChangesAsync();
        }
        return new Ctx(h, new Mock<IAssetStorage>(), panel, stageId);
    }

    private static RunThumbnailCommandHandler Handler(Ctx c) =>
        new(c.H.Db, c.H.Auth, c.H.Usage.Object, c.H.KeyProtector.Object, c.H.AiClient.Object, c.Storage.Object, c.H.Settings);

    private static Task<Result<PanelForge.Application.Features.AiAgents.Models.AiSuggestionDto>> RunAsync(Ctx c, Guid? user = null) =>
        Handler(c).Handle(new RunThumbnailCommand(AiDemoSeeder.SeriesId, c.Panel.Id, user ?? AiHarness.Artist, null), default);

    [Fact]
    public async Task Artist_GetsPendingThumbnail_StoredButNotAttached_ThenAcceptAddsNewArtworkVersion()
    {
        var c = await SetupAsync();
        var versionBefore = (await c.H.Db.Chapters.SingleAsync()).VersionVector;

        var first = await RunAsync(c);

        first.IsSuccess.Should().BeTrue(first.ErrorMessage);
        first.Value!.Kind.Should().Be(AiSuggestionKind.Thumbnail);
        first.Value.Status.Should().Be(AiSuggestionStatus.Pending);
        c.Storage.Verify(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<AssetObjectInfo?>(), It.IsAny<CancellationToken>()), Times.Once);
        (await c.H.Db.AssetReferences.CountAsync()).Should().Be(0, "AI không tự gắn ảnh vào panel");
        (await c.H.Db.WorkspaceAiConfigs.SingleAsync()).UsedImagesCurrentMonth.Should().Be(1);
        (await c.H.RunAsync(first.Value.RunId)).Status.Should().Be(AiRunStatus.Completed);

        var second = await RunAsync(c); // cùng ảnh Mock → dùng lại Asset (BR-13)
        (await c.H.ReviewAsync(first.Value.Id, AiSuggestionStatus.Accepted, AiHarness.Artist, chapterVersion: null)).IsSuccess.Should().BeTrue();
        (await c.H.ReviewAsync(second.Value!.Id, AiSuggestionStatus.Accepted, AiHarness.Artist, chapterVersion: null)).IsSuccess.Should().BeTrue();

        var refs = await c.H.Db.AssetReferences.Where(r => r.PanelId == c.Panel.Id).OrderBy(r => r.VersionNumber).ToListAsync();
        refs.Select(r => r.VersionNumber).Should().Equal(1, 2);
        refs.Should().OnlyContain(r => r.PipelineStageId == c.StageId);
        (await c.H.Db.Assets.CountAsync()).Should().Be(1);
        (await c.H.Db.Chapters.SingleAsync()).VersionVector.Should().Be(versionBefore, "artwork không phải nội dung có version");
    }

    [Fact]
    public async Task WrongRole_NoAssignment_LockedChapter_AreBlockedBeforeCallingAi()
    {
        var c = await SetupAsync(assign: false);

        (await RunAsync(c, AiHarness.Writer)).ErrorCode.Should().Be(ResultErrorCodes.Forbidden);
        (await RunAsync(c)).ErrorCode.Should().Be(ResultErrorCodes.Forbidden, "Artist chưa giữ việc cho panel");

        typeof(Chapter).GetProperty(nameof(Chapter.Status))!.SetValue(await c.H.Db.Chapters.SingleAsync(), ChapterStatus.Locked);
        await c.H.Db.SaveChangesAsync();
        (await RunAsync(c)).ErrorCode.Should().Be(ResultErrorCodes.Conflict);

        c.H.AiClient.Verify(x => x.GenerateImageAsync(It.IsAny<ImageGenRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SafetyBlocked_StoresNothing_RunFailed_NoImageCounted()
    {
        var c = await SetupAsync();
        c.H.AiClient.Setup(x => x.GenerateImageAsync(It.IsAny<ImageGenRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ImageGenResponse(false, null, null, true, null, null, DateTime.UtcNow, DateTime.UtcNow, "SAFETY_BLOCKED", "Bị chặn"));

        var result = await RunAsync(c);

        result.IsSuccess.Should().BeFalse();
        (await c.H.Db.AiSuggestions.CountAsync(s => s.Kind == AiSuggestionKind.Thumbnail)).Should().Be(0);
        (await c.H.Db.AiWorkflowRuns.SingleAsync(r => r.WorkflowType == AiWorkflowType.Thumbnail)).Status.Should().Be(AiRunStatus.Failed);
        (await c.H.Db.WorkspaceAiConfigs.SingleAsync()).UsedImagesCurrentMonth.Should().Be(0);
        c.Storage.Verify(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<AssetObjectInfo?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ImageQuotaAndConcurrentJobLimit_ReturnQuotaExceeded()
    {
        var c = await SetupAsync();
        c.H.Settings.ImageJobsPerSeries = 0;
        (await RunAsync(c)).ErrorCode.Should().Be(ResultErrorCodes.AiQuotaExceeded);

        c.H.Settings.ImageJobsPerSeries = 2;
        (await c.H.Db.WorkspaceAiConfigs.SingleAsync()).SetMonthlyImageQuota(1);
        await c.H.Db.SaveChangesAsync();
        (await RunAsync(c)).IsSuccess.Should().BeTrue();
        (await RunAsync(c)).ErrorCode.Should().Be(ResultErrorCodes.AiQuotaExceeded);
    }

    [Fact]
    public async Task Accept_Conflicts_WhenImagePromptChangedAfterGeneration()
    {
        var c = await SetupAsync();
        var result = await RunAsync(c);
        // Artist soạn và Accept prompt ảnh mới sau khi đã sinh ảnh
        var runId = await c.H.RunMockAsync(AiWorkflowType.Artist);
        var prompt = await c.H.SuggestionAsync(runId, AiSuggestionKind.ImagePrompt);
        (await c.H.ReviewAsync(prompt.Id, AiSuggestionStatus.Accepted, AiHarness.Artist,
            chapterVersion: (await c.H.Db.Chapters.SingleAsync()).VersionVector)).IsSuccess.Should().BeTrue();

        var accept = await c.H.ReviewAsync(result.Value!.Id, AiSuggestionStatus.Accepted, AiHarness.Artist, chapterVersion: null);

        accept.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
    }
}
