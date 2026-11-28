using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using PanelForge.AI.Contracts;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.AiAgents.Commands.MarkStaleRuns;
using PanelForge.Domain.Entities.Ai;
using PanelForge.Infrastructure.Persistence.Seeders;
using Xunit;

namespace PanelForge.UnitTests.Ai;

/// <summary>OPS-03: workflow nhiều agent giữ chỗ token trước khi giao cho AI service, trả lại nếu không chạy được.</summary>
public class WorkflowTokenReservationTests
{
    [Fact]
    public async Task Run_ReservesWorkflowTokens_AndHoldsThemOnRun()
    {
        var h = await AiHarness.CreateAsync();

        var result = await h.RunHandler().Handle(h.RunCommand(), default);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        h.Usage.Verify(u => u.TryReserveTokensAsync(AiDemoSeeder.WorkspaceId, h.Settings.WorkflowReservedTokens, It.IsAny<CancellationToken>()), Times.Once);
        (await h.RunAsync(result.Value!.RunId)).ReservedTokens.Should().Be(h.Settings.WorkflowReservedTokens);
    }

    [Fact]
    public async Task Run_WhenReservationFails_IsQuotaExceeded_AndStartsNothing()
    {
        var h = await AiHarness.CreateAsync();
        h.Usage.Setup(u => u.TryReserveTokensAsync(It.IsAny<Guid>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await h.RunHandler().Handle(h.RunCommand(), default);

        result.ErrorCode.Should().Be(ResultErrorCodes.AiQuotaExceeded);
        (await h.Db.AiWorkflowRuns.CountAsync()).Should().Be(0);
        h.AiClient.Verify(c => c.StartRunAsync(It.IsAny<AiRunJob>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Run_WhenAiServiceRejects_ReleasesReservation()
    {
        var h = await AiHarness.CreateAsync();
        h.AiClient.Setup(c => c.StartRunAsync(It.IsAny<AiRunJob>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AiRunAck(false, ResultErrorCodes.AiUnavailable, "AI service đang tắt."));

        await h.RunHandler().Handle(h.RunCommand(), default);

        h.Usage.Verify(u => u.RecordAsync(
            It.Is<AiUsageEntry>(e => e.ReservedTokens == h.Settings.WorkflowReservedTokens && e.PromptTokens == 0),
            It.IsAny<CancellationToken>()), Times.Once);
        (await h.Db.AiWorkflowRuns.SingleAsync()).ReservedTokens.Should().Be(0);
    }

    [Fact]
    public async Task MarkStaleRuns_ReturnsHeldTokensToWorkspace()
    {
        var h = await AiHarness.CreateAsync();
        var runId = await h.StartRunAsync();
        var config = await h.Db.WorkspaceAiConfigs.SingleAsync(c => c.WorkspaceId == AiDemoSeeder.WorkspaceId);
        config.RecordUsage(h.Settings.WorkflowReservedTokens + 500); // lúc giữ chỗ đã cộng vào bộ đếm
        typeof(AiWorkflowRun).GetProperty(nameof(AiWorkflowRun.LastActivityAtUtc))!
            .SetValue(await h.RunAsync(runId), DateTime.UtcNow.AddMinutes(-30));
        await h.Db.SaveChangesAsync();

        await new MarkStaleRunsCommandHandler(h.Db, h.Settings).Handle(new MarkStaleRunsCommand(), default);

        config.UsedTokensCurrentMonth.Should().Be(500);
        (await h.RunAsync(runId)).ReservedTokens.Should().Be(0);
    }
}
