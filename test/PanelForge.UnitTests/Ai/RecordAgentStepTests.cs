using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using PanelForge.AI.Contracts;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.AiAgents.Commands.RecordAgentStep;
using PanelForge.Domain.Enums;
using Xunit;

namespace PanelForge.UnitTests.Ai;

/// <summary>Callback nội bộ từ AI service: idempotent, kiểm scope/trình tự/hàng rào Supervisor, không ghi usage hai lần.</summary>
public class RecordAgentStepTests
{
    private static AgentStepReport Report(
        int index, string agent, int round, int? parent, string output, string provider = "Mock",
        string model = "mock-fixture-v1", long? inTokens = null, long? outTokens = null) =>
        new(index, agent, round, parent, true, provider, model, "writer@1", "Prompts/writer.md@v1#sha256:abc", "{}", "{}", output,
            DateTime.UtcNow.AddSeconds(-1), DateTime.UtcNow, inTokens, outTokens, 0, null, null);

    /// <summary>Quyết định của Supervisor (step Supervisor) chọn <paramref name="action"/>.</summary>
    private static AgentStepReport Sup(
        Guid runId, int index, string action, int round = 0, int? parent = null, string provider = "Mock",
        string model = "mock-fixture-v1", long? inTokens = null, long? outTokens = null) =>
        Report(index, "Supervisor", round, parent,
            AiJson.Serialize(new SupervisorDecision(1, runId, round, action, "lý do", null, [action], false)),
            provider, model, inTokens, outTokens);

    private static async Task<(AiHarness H, Guid RunId, AgentContext Ctx, string Draft)> ArrangeAsync(string provider = "Mock")
    {
        var h = await AiHarness.CreateAsync();
        if (provider == "Gemini")
        {
            h.Settings.Provider = "Gemini";
            h.KeyProtector.Setup(k => k.Unprotect(It.IsAny<string>())).Returns("k");
            (await h.Db.WorkspaceAiConfigs.SingleAsync()).UpdateSettings("Google Gemini", "cipher", true, 1_000_000, "[\"gemini-2.5-flash\"]");
            await h.Db.SaveChangesAsync();
        }
        var runId = await h.StartRunAsync();
        var ctx = (await new PanelForge.Application.Features.AiAgents.Queries.GetAgentContext.GetAgentContextQueryHandler(h.Db)
            .Handle(new(runId), default)).Value!;
        var an = ctx.Bible.Single(b => b.Code == "AN");
        var draft = AiJson.Serialize(new ScriptDraft(1, runId, ctx.SceneId, 0,
            [new ScriptDraftLine(Guid.NewGuid(), 1, an.EntryId, "Xin chào", null)], ctx.BibleRevisionIds, null));
        return (h, runId, ctx, draft);
    }

    private static Task<Result<AgentStepAck>> Send(AiHarness h, Guid runId, AgentStepReport r) =>
        h.RecordHandler().Handle(new RecordAgentStepCommand(runId, r), default);

    /// <summary>Supervisor chọn Writer (step 1) rồi Writer chạy (step 2).</summary>
    private static async Task WriterRunsAsync(AiHarness h, Guid runId, string draft, string provider = "Mock", string model = "mock-fixture-v1",
        long? inTokens = null, long? outTokens = null)
    {
        (await Send(h, runId, Sup(runId, 1, "Writer", provider: provider, model: model))).IsSuccess.Should().BeTrue();
        (await Send(h, runId, Report(2, "Writer", 0, null, draft, provider, model, inTokens, outTokens))).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task DuplicateCallback_DoesNotDuplicateStep_OrUsage()
    {
        var (h, runId, _, draft) = await ArrangeAsync("Gemini");
        var model = h.Settings.GeminiModel;
        (await Send(h, runId, Sup(runId, 1, "Writer", provider: "Gemini", model: model))).IsSuccess.Should().BeTrue();
        var report = Report(2, "Writer", 0, null, draft, "Gemini", model, inTokens: 120, outTokens: 80);

        var first = await Send(h, runId, report);
        var again = await Send(h, runId, report);
        var third = await Send(h, runId, report);

        first.IsSuccess.Should().BeTrue(first.ErrorMessage);
        first.Value!.Duplicate.Should().BeFalse();
        again.Value!.Duplicate.Should().BeTrue();
        third.Value!.StepId.Should().Be(first.Value.StepId);
        (await h.StepsAsync(runId)).Should().HaveCount(2);
        h.Usage.Verify(u => u.RecordAsync(It.Is<AiUsageEntry>(e => e.PromptTokens == 120 && e.CompletionTokens == 80
            && e.Feature == "AiAgents.Writer" && e.UserId == AiHarness.Writer), default), Times.Once);
        (await h.StepsAsync(runId)).Last().UsageRecordId.Should().NotBeNull();
    }

    [Fact]
    public async Task MockRun_NeverRecordsUsage_EvenIfClientClaimsTokens()
    {
        var (h, runId, _, draft) = await ArrangeAsync();

        await WriterRunsAsync(h, runId, draft, inTokens: 999, outTokens: 999);

        h.Usage.Verify(u => u.RecordAsync(It.IsAny<AiUsageEntry>(), default), Times.Never);
        var step = (await h.StepsAsync(runId)).Last();
        step.PromptTokens.Should().BeNull();
        step.UsageRecordId.Should().BeNull();
    }

    [Fact]
    public async Task OutOfOrderOrUnknownRun_IsRejected()
    {
        var (h, runId, _, _) = await ArrangeAsync();

        (await Send(h, runId, Sup(runId, 2, "Writer"))).ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        (await Send(h, Guid.NewGuid(), Sup(runId, 1, "Writer"))).ErrorCode.Should().Be(ResultErrorCodes.NotFound);
        (await h.StepsAsync(runId)).Should().BeEmpty();
    }

    [Fact]
    public async Task Step_WithIdsOutsideRunScope_IsRejected_AndNotStored()
    {
        var (h, runId, ctx, _) = await ArrangeAsync();
        (await Send(h, runId, Sup(runId, 1, "Writer"))).IsSuccess.Should().BeTrue();
        var foreign = AiJson.Serialize(new ScriptDraft(1, runId, ctx.SceneId, 0,
            [new ScriptDraftLine(Guid.NewGuid(), 1, Guid.NewGuid() /* speaker ngoài Bible */, "Xin chào", null)],
            ctx.BibleRevisionIds, null));
        var wrongScene = AiJson.Serialize(new ScriptDraft(1, runId, Guid.NewGuid() /* scene khác */, 0,
            [new ScriptDraftLine(Guid.NewGuid(), 1, null, "x", null)], ctx.BibleRevisionIds, null));
        var wrongBible = AiJson.Serialize(new ScriptDraft(1, runId, ctx.SceneId, 0,
            [new ScriptDraftLine(Guid.NewGuid(), 1, null, "x", null)], [Guid.NewGuid()], null));

        foreach (var bad in new[] { foreign, wrongScene, wrongBible })
            (await Send(h, runId, Report(2, "Writer", 0, null, bad))).ErrorCode.Should().Be(ResultErrorCodes.Validation);

        (await h.StepsAsync(runId)).Should().HaveCount(1, "chỉ có quyết định của Supervisor");
    }

    [Fact]
    public async Task Step_WithBrokenJson_OrMismatchedProvider_IsRejected()
    {
        var (h, runId, _, draft) = await ArrangeAsync();
        (await Send(h, runId, Sup(runId, 1, "Writer"))).IsSuccess.Should().BeTrue();

        (await Send(h, runId, Report(2, "Writer", 0, null, "{ hỏng"))).ErrorCode.Should().Be(ResultErrorCodes.Validation);
        (await Send(h, runId, Report(2, "Writer", 0, null, draft, provider: "Gemini"))).ErrorCode.Should().Be(ResultErrorCodes.Validation);
        (await h.StepsAsync(runId)).Should().HaveCount(1);
    }

    // ── Hàng rào của Supervisor ─────────────────────────────────────────────

    [Fact]
    public async Task Agent_CannotRun_WithoutSupervisorDecision_OrAgainstIt()
    {
        var (h, runId, _, draft) = await ArrangeAsync();

        // Writer chạy thẳng, không có quyết định điều phối
        (await Send(h, runId, Report(1, "Writer", 0, null, draft))).ErrorCode.Should().Be(ResultErrorCodes.Validation);

        // Supervisor chọn Writer nhưng agent gửi lên là Continuity
        (await Send(h, runId, Sup(runId, 1, "Writer"))).IsSuccess.Should().BeTrue();
        var r = await Send(h, runId, Report(2, "Continuity", 0, null, "{}"));
        r.ErrorCode.Should().Be(ResultErrorCodes.Validation);
        (await h.StepsAsync(runId)).Should().HaveCount(1);
    }

    [Theory]
    [InlineData("Storyboard")]        // nhảy cóc: chưa có script
    [InlineData("Editor")]
    [InlineData("Finish")]            // kết thúc khi chưa làm gì
    [InlineData("StopForHuman")]      // dừng khi không có finding chặn
    [InlineData("Hack")]
    public async Task Supervisor_ChoosingActionOutsideGuardrail_IsRejected(string action)
    {
        var (h, runId, _, _) = await ArrangeAsync();

        var r = await Send(h, runId, Sup(runId, 1, action));

        r.ErrorCode.Should().Be(ResultErrorCodes.Validation);
        r.ErrorMessage.Should().Contain("không nằm trong danh sách được phép");
        (await h.StepsAsync(runId)).Should().BeEmpty();
    }

    [Fact]
    public async Task Supervisor_CannotDecideTwiceInARow_OrAfterWriter_SkippingContinuity()
    {
        var (h, runId, _, draft) = await ArrangeAsync();
        (await Send(h, runId, Sup(runId, 1, "Writer"))).IsSuccess.Should().BeTrue();

        // quyết định trước chưa được thực thi
        (await Send(h, runId, Sup(runId, 2, "Writer", parent: 1))).ErrorCode.Should().Be(ResultErrorCodes.Validation);

        (await Send(h, runId, Report(2, "Writer", 0, null, draft))).IsSuccess.Should().BeTrue();
        // sau Writer chỉ được gọi Continuity, không được đi thẳng Storyboard / Writer lượt sửa
        (await Send(h, runId, Sup(runId, 3, "Storyboard", parent: 2))).ErrorCode.Should().Be(ResultErrorCodes.Validation);
        (await Send(h, runId, Sup(runId, 3, "Writer", parent: 2))).ErrorCode.Should().Be(ResultErrorCodes.Validation);
        (await Send(h, runId, Sup(runId, 3, "Continuity", parent: 2))).IsSuccess.Should().BeTrue();
        (await h.StepsAsync(runId)).Should().HaveCount(3);
    }

    [Fact]
    public async Task FailedStep_IsStored_AndMarksRunFailed_NoSuggestions()
    {
        var (h, runId, _, _) = await ArrangeAsync();
        var failed = new AgentStepReport(1, "Writer", 0, null, false, "Mock", "mock-fixture-v1", "writer@1", "ref", "{}", "{}", null,
            DateTime.UtcNow, DateTime.UtcNow, null, null, 1, "INVALID_OUTPUT", "Output không hợp lệ.");

        var r = await Send(h, runId, failed);

        r.Value!.ShouldContinue.Should().BeFalse();
        r.Value.RunStatus.Should().Be("Failed");
        (await h.RunAsync(runId)).ErrorCode.Should().Be("INVALID_OUTPUT");
        h.Db.AiSuggestions.Count().Should().Be(0);
        // sau khi run kết thúc, step mới bị từ chối
        (await Send(h, runId, failed with { StepIndex = 2 })).ErrorCode.Should().Be(ResultErrorCodes.Conflict);
    }

    [Fact]
    public async Task GeminiQuotaExhaustedMidRun_StopsRun_AndTellsAiServiceNotToContinue()
    {
        var (h, runId, _, draft) = await ArrangeAsync("Gemini");
        var model = h.Settings.GeminiModel;
        (await Send(h, runId, Sup(runId, 1, "Writer", provider: "Gemini", model: model))).IsSuccess.Should().BeTrue();
        h.Usage.Setup(u => u.CheckAvailabilityAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AiAvailability.Blocked(AiUsageStatus.QuotaExceeded, "Hết hạn mức."));

        var r = await Send(h, runId, Report(2, "Writer", 0, null, draft, "Gemini", model, 100, 50));

        r.Value!.ShouldContinue.Should().BeFalse();
        var run = await h.RunAsync(runId);
        run.Status.Should().Be(AiRunStatus.Failed);
        run.ErrorCode.Should().Be(ResultErrorCodes.AiQuotaExceeded);
    }

    [Fact]
    public async Task SupervisorTokens_AreRecordedAsUsage_ForGemini()
    {
        var (h, runId, _, _) = await ArrangeAsync("Gemini");

        await Send(h, runId, Sup(runId, 1, "Writer", provider: "Gemini", model: h.Settings.GeminiModel, inTokens: 300, outTokens: 40));

        h.Usage.Verify(u => u.RecordAsync(It.Is<AiUsageEntry>(e => e.Feature == "AiAgents.Supervisor"
            && e.PromptTokens == 300 && e.CompletionTokens == 40), default), Times.Once);
    }
}
