using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using PanelForge.AI.Agents;
using PanelForge.AI.Contracts;
using PanelForge.AI.Demo;
using PanelForge.AI.Gateway;
using PanelForge.Application.Common;
using PanelForge.Application.Features.AiAgents.Commands.RunImpactSuggestion;
using PanelForge.Application.Features.ScriptVersions;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Enums;
using PanelForge.Infrastructure.Persistence.Seeders;
using Xunit;

namespace PanelForge.UnitTests.Ai;

/// <summary>Smart Ripple: chỉ xét cờ do sửa kịch bản, gợi ý có căn cứ và Producer quyết định bằng Accept.</summary>
public sealed class ImpactSuggestionTests
{
    private static async Task<(AiHarness H, StageTask Cell)> SetupAsync(string reason = StageTask.ScriptChangedReason)
    {
        var h = await AiHarness.CreateAsync();
        h.AiClient.Setup(c => c.RunCopilotAsync(It.IsAny<CopilotRequest>(), It.IsAny<CancellationToken>()))
            .Returns((CopilotRequest r, CancellationToken ct) =>
                new CopilotAgent(new AgentRunner(new MockChatClient(), Options.Create(new ProviderOptions()))).RunAsync(r, ct));
        await h.AcceptScriptAndLayoutAsync();

        var rows = (await ScriptSnapshotBuilder.BuildAsync(h.Db, AiDemoSeeder.ChapterId, default)).Rows;
        var edited = rows.Select((r, i) => i == 0 ? r with { Text = r.Text + " (bản cũ)" } : r).ToList();
        h.Db.ScriptVersions.Add(ScriptVersion.Create(AiDemoSeeder.ChapterId, 90, "Bản cũ", AiHarness.Writer, "hash",
            ScriptSnapshotBuilder.Serialize(edited), edited.Count));

        var series = await h.Db.Series.SingleAsync(s => s.Id == AiDemoSeeder.SeriesId);
        var pipeline = PipelineDefinition.Create(series.WorkspaceId, "Pipeline", seriesId: series.Id);
        var stage = PipelineStage.Create(pipeline.Id, "Pencil", "pencil", 1, allowedRole: WorkspaceRole.Artist, isInitial: true);
        h.Db.PipelineDefinitions.Add(pipeline);
        h.Db.PipelineStages.Add(stage);
        var page = await h.Db.Pages.FirstAsync(p => p.ChapterId == AiDemoSeeder.ChapterId && p.PageNumber == rows[0].Page);
        var lot = StageTaskLot.Create(series.Id, AiDemoSeeder.ChapterId, stage.Id, AiHarness.Artist, WorkspaceRole.Artist, Guid.NewGuid(), null, null);
        var cell = StageTask.Create(series.Id, AiDemoSeeder.ChapterId, stage.Id, page.Id);
        cell.AssignTo(lot.Id);
        cell.Start(prerequisitesApproved: true);
        cell.MarkInputOutdated(reason);
        h.Db.StageTaskLots.Add(lot);
        h.Db.StageTasks.Add(cell);
        await h.Db.SaveChangesAsync();
        return (h, cell);
    }

    private static RunImpactSuggestionCommandHandler Handler(AiHarness h) =>
        new(h.Db, h.Auth, h.Usage.Object, h.KeyProtector.Object, h.AiClient.Object, h.Settings);

    [Fact]
    public async Task Suggestion_ChangesNothing_UntilProducerAccepts()
    {
        var (h, cell) = await SetupAsync();

        var result = await Handler(h).Handle(new RunImpactSuggestionCommand(AiDemoSeeder.ChapterId, cell.Id, AiHarness.Producer), default);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value!.Kind.Should().Be(AiSuggestionKind.ImpactRipple);
        cell.IsInputOutdated.Should().BeTrue();
        var accepted = await h.ReviewAsync(result.Value.Id, AiSuggestionStatus.Accepted, AiHarness.Producer, null);
        accepted.IsSuccess.Should().BeTrue(accepted.ErrorMessage);
        cell.IsInputOutdated.Should().BeFalse();
    }

    [Fact]
    public async Task OnlyProducer_CanRequest()
    {
        var (h, cell) = await SetupAsync();
        (await Handler(h).Handle(new RunImpactSuggestionCommand(AiDemoSeeder.ChapterId, cell.Id, AiHarness.Editor), default))
            .ErrorCode.Should().Be(ResultErrorCodes.Forbidden);
    }

    [Fact]
    public async Task OtherOutdatedReasons_AreOutOfScope_AndDoNotCallAi()
    {
        var (h, cell) = await SetupAsync("Công đoạn trước được mở lại.");

        (await Handler(h).Handle(new RunImpactSuggestionCommand(AiDemoSeeder.ChapterId, cell.Id, AiHarness.Producer), default))
            .ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        h.AiClient.Verify(c => c.RunCopilotAsync(It.IsAny<CopilotRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
