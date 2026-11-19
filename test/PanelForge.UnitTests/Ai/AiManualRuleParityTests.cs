using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using PanelForge.AI.Contracts;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.Consistency.Commands.CheckPanelImageConsistency;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Entities.Workspaces;
using PanelForge.Domain.Enums;
using PanelForge.Infrastructure.Persistence.Seeders;
using Xunit;

namespace PanelForge.UnitTests.Ai;

/// <summary>AI theo cùng luật với thao tác tay (BR-07, BR-12, §5.1): quyền áp dụng, Frozen, lưới V3, liên kết Panel.</summary>
public sealed class AiManualRuleParityTests
{
    private static async Task<Chapter> ChapterAsync(AiHarness h) => await h.Db.Chapters.SingleAsync(c => c.Id == AiDemoSeeder.ChapterId);

    private static async Task FreezeAsync(AiHarness h)
    {
        h.Db.Entry(await ChapterAsync(h)).Property(c => c.ScriptStatus).CurrentValue = ScriptStatus.Frozen;
        await h.Db.SaveChangesAsync();
    }

    private static async Task<StageTask> AddCellAsync(AiHarness h, Guid pageId, Guid? assignee, WorkspaceRole role = WorkspaceRole.Artist)
    {
        var stage = await h.Db.PipelineStages.FirstOrDefaultAsync();
        var stageId = stage?.Id ?? Guid.NewGuid();
        var cell = StageTask.Create(AiDemoSeeder.SeriesId, AiDemoSeeder.ChapterId, stageId, pageId);
        if (assignee is { } user)
        {
            var lot = StageTaskLot.Create(AiDemoSeeder.SeriesId, AiDemoSeeder.ChapterId, stageId, user, role, Guid.NewGuid(), null, null);
            h.Db.StageTaskLots.Add(lot);
            cell.AssignTo(lot.Id);
            cell.Start(prerequisitesApproved: true);
        }
        h.Db.StageTasks.Add(cell);
        await h.Db.SaveChangesAsync();
        return cell;
    }

    [Theory]
    [InlineData("Producer")]
    [InlineData("Editor")]
    public async Task ProducerAndEditor_CannotApplyScript_ButCanReject(string who)
    {
        var h = await AiHarness.CreateAsync();
        var script = await h.SuggestionAsync(await h.CompleteDemoRunAsync(), AiSuggestionKind.Script);
        var user = who == "Producer" ? AiHarness.Producer : AiHarness.Editor;

        (await h.ReviewAsync(script.Id, AiSuggestionStatus.Accepted, user)).ErrorCode.Should().Be(ResultErrorCodes.Forbidden);
        (await h.ReviewAsync(script.Id, AiSuggestionStatus.Rejected, user)).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task FrozenScript_BlocksAcceptAndNewScriptRun()
    {
        var h = await AiHarness.CreateAsync();
        var script = await h.SuggestionAsync(await h.CompleteDemoRunAsync(), AiSuggestionKind.Script);
        await FreezeAsync(h);

        (await h.ReviewAsync(script.Id, AiSuggestionStatus.Accepted, AiHarness.Writer)).ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        var runs = await h.Db.AiWorkflowRuns.CountAsync();
        await FluentActions.Invoking(() => h.RunMockAsync(AiWorkflowType.Script)).Should().ThrowAsync<Exception>();
        (await h.Db.AiWorkflowRuns.CountAsync()).Should().Be(runs);
    }

    [Fact]
    public async Task AcceptScript_IsBlocked_WhenSceneHasLinkedLine()
    {
        var h = await AiHarness.CreateAsync();
        var script = await h.SuggestionAsync(await h.CompleteDemoRunAsync(), AiSuggestionKind.Script);
        var line = ScriptLine.Create(AiDemoSeeder.SceneId, 99, "Thoại đã gắn", null, Guid.NewGuid());
        line.AssignToPanel(Guid.NewGuid());
        h.Db.ScriptLines.Add(line);
        await h.Db.SaveChangesAsync();

        (await h.ReviewAsync(script.Id, AiSuggestionStatus.Accepted, AiHarness.Writer)).ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        (await h.Db.ScriptLines.AnyAsync(l => l.Id == line.Id)).Should().BeTrue();
    }

    [Fact]
    public async Task AcceptLayout_OnGridChapter_CreatesCellsForNewPages()
    {
        var h = await AiHarness.CreateAsync();
        var runId = await h.CompleteDemoRunAsync();
        var script = await h.SuggestionAsync(runId, AiSuggestionKind.Script);
        var layout = await h.SuggestionAsync(runId, AiSuggestionKind.Layout);
        var series = await h.Db.Series.SingleAsync(s => s.Id == AiDemoSeeder.SeriesId);
        var pipeline = PipelineDefinition.Create(series.WorkspaceId, "Pipeline", seriesId: series.Id);
        h.Db.PipelineDefinitions.Add(pipeline);
        h.Db.PipelineStages.Add(PipelineStage.Create(pipeline.Id, "Pencil", "pencil", 1, allowedRole: WorkspaceRole.Artist, isInitial: true));
        await h.Db.SaveChangesAsync();
        await AddCellAsync(h, Guid.NewGuid(), null);

        (await h.ReviewAsync(script.Id, AiSuggestionStatus.Accepted, AiHarness.Writer, 0)).IsSuccess.Should().BeTrue();
        var accepted = await h.ReviewAsync(layout.Id, AiSuggestionStatus.Accepted, AiHarness.Writer, 1);

        accepted.IsSuccess.Should().BeTrue(accepted.ErrorMessage);
        var pageIds = await h.Db.Pages.Where(p => p.ChapterId == AiDemoSeeder.ChapterId).Select(p => p.Id).ToListAsync();
        pageIds.Should().NotBeEmpty();
        foreach (var pageId in pageIds)
            (await h.Db.StageTasks.AnyAsync(t => t.PageId == pageId)).Should().BeTrue();
    }

    [Theory]
    [InlineData(false, false, true)]   // ô của Artist khác
    [InlineData(true, true, true)]     // ô của mình nhưng đầu vào lỗi thời
    [InlineData(true, false, false)]   // đang giữ ô hợp lệ
    public async Task ArtistAccept_FollowsGridCell(bool mine, bool outdated, bool blocked)
    {
        var h = await AiHarness.CreateAsync();
        await h.AcceptScriptAndLayoutAsync();
        var prompt = await h.SuggestionAsync(await h.RunMockAsync(AiWorkflowType.Artist), AiSuggestionKind.ImagePrompt);
        var page = await h.Db.Pages.FirstAsync(p => p.ChapterId == AiDemoSeeder.ChapterId);
        var cell = await AddCellAsync(h, page.Id, mine ? AiHarness.Artist : Guid.NewGuid());
        if (outdated)
        {
            cell.MarkInputOutdated("Kịch bản đổi");
            await h.Db.SaveChangesAsync();
        }

        var result = await h.ReviewAsync(prompt.Id, AiSuggestionStatus.Accepted, AiHarness.Artist, 2);

        if (blocked) result.ErrorCode.Should().Be(ResultErrorCodes.Forbidden);
        else result.IsSuccess.Should().BeTrue(result.ErrorMessage);
    }

    [Fact]
    public async Task PanelImageCheck_OnReadOnlyChapter_DoesNotCallVision()
    {
        var h = await AiHarness.CreateAsync();
        await h.AcceptScriptAndLayoutAsync();
        var panel = await h.Db.Panels.FirstAsync();
        h.Db.Entry(await ChapterAsync(h)).Property(c => c.Status).CurrentValue = ChapterStatus.ChapterApproved;
        await h.Db.SaveChangesAsync();
        var vision = new Mock<IImageConsistencyService>(MockBehavior.Strict);

        var result = await new CheckPanelImageConsistencyCommandHandler(h.Db, vision.Object)
            .Handle(new CheckPanelImageConsistencyCommand(panel.Id, AiHarness.Editor, null, null), default);

        result.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
    }

    [Fact]
    public void DefaultAllowedModels_IncludeRunningTextAndImageModels()
    {
        var settings = new PanelForge.Application.Features.AiAgents.Models.AiAgentSettings();
        WorkspaceAiConfig.DefaultAllowedModels.Should().Contain([settings.GeminiModel, settings.GeminiImageModel]);
        WorkspaceAiConfig.Create(Guid.NewGuid()).AllowedModelsJson.Should().Be(WorkspaceAiConfig.DefaultAllowedModelsJson);
    }
}
