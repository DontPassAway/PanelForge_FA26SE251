using FluentAssertions;
using Microsoft.Extensions.AI;
using Moq;
using PanelForge.AI.Contracts;
using PanelForge.AI.Demo;
using PanelForge.AI.Demo.Fixtures;
using PanelForge.Domain.Entities.Ai;
using PanelForge.Domain.Enums;
using Xunit;

namespace PanelForge.UnitTests.Ai;

/// <summary>
/// Workflow do Supervisor (Gemini/Mock) điều phối Writer → Continuity → Storyboard → Editor, chạy thật (Mock fixture) qua hợp đồng HTTP
/// nối thẳng vào handler của Backend. Mỗi agent chuyên môn chỉ chạy sau một quyết định của Supervisor nằm trong hàng rào của code.
/// </summary>
public class DemoWorkflowTests
{
    private static List<AiAgentStep> Workers(IEnumerable<AiAgentStep> steps) =>
        steps.Where(s => s.Agent != AiAgentType.Supervisor).ToList();

    private static List<SupervisorDecision> Decisions(IEnumerable<AiAgentStep> steps) =>
        steps.Where(s => s.Agent == AiAgentType.Supervisor)
            .Select(s => AiJson.Deserialize<SupervisorDecision>(s.OutputJson!)!).ToList();

    private static string Ask(IChatClient mock, string msg) =>
        mock.GetResponseAsync([new ChatMessage(ChatRole.User, msg)]).Result.Text;

    [Fact]
    public async Task HappyPath_SupervisorOrchestrates_WriterWrong_ContinuityFinds_WriterFixes_ThenStoryboardAndEditor()
    {
        var h = await AiHarness.CreateAsync();
        var runId = await h.CompleteDemoRunAsync();

        var all = await h.StepsAsync(runId);
        // Supervisor quyết định TRƯỚC mỗi agent và chốt bằng Finish: S W S C S W S C S Sb S E S
        all.Select(s => s.Agent).Should().Equal(
            AiAgentType.Supervisor, AiAgentType.Writer, AiAgentType.Supervisor, AiAgentType.Continuity,
            AiAgentType.Supervisor, AiAgentType.Writer, AiAgentType.Supervisor, AiAgentType.Continuity,
            AiAgentType.Supervisor, AiAgentType.Storyboard, AiAgentType.Supervisor, AiAgentType.Editor,
            AiAgentType.Supervisor);
        all.Should().OnlyContain(s => s.Status == AiStepStatus.Succeeded);

        var decisions = Decisions(all);
        decisions.Select(d => d.Action).Should().Equal(
            "Writer", "Continuity", "Writer", "Continuity", "Storyboard", "Editor", WorkflowRules.ActionFinish);
        decisions.Should().OnlyContain(d => !d.GuardrailOverridden && d.Reason.Length > 0 && d.AllowedActions.Contains(d.Action));
        decisions[2].Reason.Should().Contain("Writer sửa");           // sau finding chặn: Supervisor chọn sửa thay vì dừng
        decisions[2].Instruction.Should().Contain("mâu thuẫn");       // và chỉ đạo Writer cần sửa gì
        decisions[2].AllowedActions.Should().BeEquivalentTo(["Writer", WorkflowRules.ActionStopForHuman]);

        var steps = Workers(all);
        steps.Select(s => (s.Agent, s.Round)).Should().Equal(
            (AiAgentType.Writer, 0), (AiAgentType.Continuity, 0), (AiAgentType.Writer, 1),
            (AiAgentType.Continuity, 1), (AiAgentType.Storyboard, 0), (AiAgentType.Editor, 0));

        // Chuỗi quan hệ giữa các lượt: mỗi step trỏ về step cung cấp input chính của nó
        steps[0].ParentStepId.Should().BeNull();
        steps[1].ParentStepId.Should().Be(steps[0].Id);   // Continuity kiểm bản Writer đầu
        steps[2].ParentStepId.Should().Be(steps[1].Id);   // Writer sửa theo finding
        steps[3].ParentStepId.Should().Be(steps[2].Id);   // Continuity kiểm lại bản sửa
        steps[4].ParentStepId.Should().Be(steps[3].Id);   // Storyboard dựa trên script đã sạch
        steps[5].ParentStepId.Should().Be(steps[4].Id);   // Editor dựa trên layout

        // Writer sai → Continuity phát hiện có bằng chứng + liên kết dòng
        var draft0 = AiJson.Deserialize<ScriptDraft>(steps[0].OutputJson!)!;
        draft0.Lines.Any(l => (l.Dialogue + l.StageDirection).Contains("chìa khóa bạc")).Should().BeTrue();
        var report0 = AiJson.Deserialize<ContinuityReport>(steps[1].OutputJson!)!;
        report0.HasBlockingFindings.Should().BeTrue();
        var finding = report0.Findings.Single();
        finding.ScriptLineIds.Should().OnlyContain(id => draft0.Lines.Any(l => l.LineId == id));
        finding.Evidence.Should().NotBeEmpty().And.OnlyContain(e => e.Quote.Contains("Chương 5"));
        var run = await h.RunAsync(runId);
        finding.Evidence.Select(e => e.BibleRevisionId).Should().BeSubsetOf(AiJson.Deserialize<List<Guid>>(run.BibleRevisionIdsJson)!);

        // Output của bước trước là input của bước sau
        var draft1 = AiJson.Deserialize<ScriptDraft>(steps[2].OutputJson!)!;
        draft1.Lines.Any(l => (l.Dialogue + l.StageDirection).Contains("chìa khóa bạc")).Should().BeFalse();
        steps[2].InputJson.Should().Contain(draft0.Lines[3].LineId.ToString());          // Writer sửa nhận bản nháp + finding trước
        steps[2].InputJson.Should().Contain("blockingFindings");
        foreach (var l in draft1.Lines) steps[3].InputJson.Should().Contain(l.LineId.ToString());   // Continuity kiểm đúng bản sửa
        foreach (var l in draft1.Lines) steps[4].InputJson.Should().Contain(l.LineId.ToString());   // Storyboard nhận script cuối
        var layout = AiJson.Deserialize<StoryboardLayout>(steps[4].OutputJson!)!;
        layout.Pages.SelectMany(p => p.Panels).SelectMany(p => p.SourceScriptLineIds).Distinct()
            .Should().BeEquivalentTo(draft1.Lines.Select(l => l.LineId));
        layout.ReadingDirection.Should().Be("RightToLeft");
        steps[5].InputJson.Should().Contain("checklist");

        // Provenance: prompt version/ref, parameters; Mock không khai token
        all.Should().OnlyContain(s => s.PromptVersion != "" && s.PromptRef.Contains("#sha256:"));
        steps.Should().OnlyContain(s => s.ParametersJson.Contains("temperature"));
        // Tiết kiệm quota: Supervisor chỉ gọi model ở nhánh rẽ thật (sửa tiếp hay dừng); các quyết định một-lựa-chọn do code lập
        var supervisorSteps = all.Where(s => s.Agent == AiAgentType.Supervisor).ToList();
        supervisorSteps.Select(s => s.ParametersJson.Contains("temperature")).Should().Equal(false, false, true, false, false, false, false);
        supervisorSteps.Where(s => !s.ParametersJson.Contains("temperature"))
            .Should().OnlyContain(s => s.ParametersJson.Contains("single-allowed-action"));
        all.Should().OnlyContain(s => s.PromptTokens == null && s.CompletionTokens == null && s.Provider == "Mock");
        all.Where(s => s.Agent == AiAgentType.Supervisor).Should().OnlyContain(s => s.PromptVersion == "supervisor@4");
        h.Usage.Verify(u => u.RecordAsync(It.IsAny<PanelForge.Application.Common.Interfaces.Services.AiUsageEntry>(), default), Times.Never);

        run.Status.Should().Be(AiRunStatus.Completed);
        run.RevisionRounds.Should().Be(1);
        // Kết quả cuối: đề xuất chờ người quyết định, chưa áp dụng gì
        var script = await h.SuggestionAsync(runId, AiSuggestionKind.Script);
        var lay = await h.SuggestionAsync(runId, AiSuggestionKind.Layout);
        script.Status.Should().Be(AiSuggestionStatus.Pending);
        lay.Status.Should().Be(AiSuggestionStatus.Pending);
        lay.ParentSuggestionId.Should().Be(script.Id);
        script.HasBlockingFindings.Should().BeFalse();
        h.Db.ScriptLines.Count().Should().Be(0, "Accept mới là bước áp dụng nội dung");
        h.Db.ContentRevisions.Count().Should().Be(0);
        h.Db.Chapters.Single().Status.Should().Be(ChapterStatus.InProduction);
    }

    [Fact]
    public async Task SupervisorInstruction_IsPassedToTheNextAgent()
    {
        var h = await AiHarness.CreateAsync();
        var runId = await h.StartRunAsync();
        var mock = new MockChatClient();
        var chat = new ScriptedChatClient((_, msg) => Ask(mock, msg));

        await h.ExecuteWorkflowAsync(runId, chat);

        var revisionWriter = chat.UserMessages.Single(m => m.Contains("[[agent:writer]]") && m.Contains("[[round:1]]"));
        revisionWriter.Should().Contain("Chỉ đạo của Supervisor");
        revisionWriter.Should().Contain("Bỏ chi tiết mâu thuẫn với Bible");
        // Lượt đầu chỉ có một bước hợp lệ → code quyết định, không gọi model nên không có chỉ đạo
        var firstWriter = chat.UserMessages.First(m => m.Contains("[[agent:writer]]"));
        firstWriter.Should().NotContain("Chỉ đạo của Supervisor");
        chat.UserMessages.Count(m => m.Contains("[[agent:supervisor]]")).Should().Be(1);
    }

    [Fact]
    public async Task Stubborn_StopsAfterTwoRevisionRounds_WaitingForHuman_NoStoryboard()
    {
        var h = await AiHarness.CreateAsync();
        var runId = await h.StartRunAsync($"Viết cảnh bà Lan nhìn An. {MockFixtures.StubbornMarker}");
        await h.ExecuteWorkflowAsync(runId);

        var all = await h.StepsAsync(runId);
        var steps = Workers(all);
        steps.Select(s => (s.Agent, s.Round)).Should().Equal(
            (AiAgentType.Writer, 0), (AiAgentType.Continuity, 0),
            (AiAgentType.Writer, 1), (AiAgentType.Continuity, 1),
            (AiAgentType.Writer, 2), (AiAgentType.Continuity, 2));
        steps.Should().NotContain(s => s.Agent == AiAgentType.Storyboard || s.Agent == AiAgentType.Editor);

        // Hết lượt sửa: hàng rào chỉ còn StopForHuman → Supervisor chốt dừng
        var decisions = Decisions(all);
        decisions.Last().Action.Should().Be(WorkflowRules.ActionStopForHuman);
        decisions.Last().AllowedActions.Should().Equal(WorkflowRules.ActionStopForHuman);
        decisions.Select(d => d.Action).Should().Equal(
            "Writer", "Continuity", "Writer", "Continuity", "Writer", "Continuity", WorkflowRules.ActionStopForHuman);

        // Mỗi lượt sửa lưu riêng, id dòng khác nhau giữa các lượt
        var ids = steps.Where(s => s.Agent == AiAgentType.Writer)
            .Select(s => AiJson.Deserialize<ScriptDraft>(s.OutputJson!)!.Lines[0].LineId).Distinct();
        ids.Should().HaveCount(3);

        var run = await h.RunAsync(runId);
        run.Status.Should().Be(AiRunStatus.WaitingForHuman);
        run.RevisionRounds.Should().Be(2);
        var script = await h.SuggestionAsync(runId, AiSuggestionKind.Script);
        script.HasBlockingFindings.Should().BeTrue();
        h.Db.AiSuggestions.Count(s => s.RunId == runId).Should().Be(1, "không có layout khi chưa qua Continuity");
    }

    [Fact]
    public async Task Supervisor_CanEscalateToHumanEarly_InsteadOfAnotherRevision()
    {
        var h = await AiHarness.CreateAsync();
        var runId = await h.StartRunAsync($"Viết cảnh bà Lan nhìn An. {MockFixtures.StubbornMarker} {MockFixtures.EscalateMarker}");
        await h.ExecuteWorkflowAsync(runId);

        var all = await h.StepsAsync(runId);
        Workers(all).Select(s => s.Agent).Should().Equal(AiAgentType.Writer, AiAgentType.Continuity);
        var last = Decisions(all).Last();
        last.Action.Should().Be(WorkflowRules.ActionStopForHuman);
        last.AllowedActions.Should().Contain("Writer", "lúc đó vẫn còn lượt sửa: dừng sớm là lựa chọn của Supervisor");
        last.Reason.Should().Contain("leo thang");
        var run = await h.RunAsync(runId);
        run.Status.Should().Be(AiRunStatus.WaitingForHuman);
        run.RevisionRounds.Should().Be(0);
        (await h.SuggestionAsync(runId, AiSuggestionKind.Script)).HasBlockingFindings.Should().BeTrue();
    }

    [Fact]
    public async Task Supervisor_PickingAnIllegalAction_IsRepairedOnce_ThenOverriddenByGuardrail_AndRecorded()
    {
        var h = await AiHarness.CreateAsync();
        var runId = await h.StartRunAsync();
        var mock = new MockChatClient();
        // Ở nhánh rẽ thật (sau finding chặn: Writer | StopForHuman) Supervisor đòi nhảy thẳng sang Editor (ngoài danh sách cho phép)
        var chat = new ScriptedChatClient((_, msg) => msg.Contains("[[agent:supervisor]]")
            ? "{\"action\":\"Editor\",\"reason\":\"viết review luôn\",\"instruction\":null}"
            : Ask(mock, msg));

        await h.ExecuteWorkflowAsync(runId, chat);

        var all = await h.StepsAsync(runId);
        var first = all.Where(s => s.Agent == AiAgentType.Supervisor).ElementAt(2);
        first.RepairAttempts.Should().Be(1, "cho một lần sửa output rồi mới ghi đè");
        var decision = AiJson.Deserialize<SupervisorDecision>(first.OutputJson!)!;
        decision.GuardrailOverridden.Should().BeTrue();
        decision.Action.Should().Be("Writer", "hành động an toàn đầu tiên trong hàng rào");
        decision.Reason.Should().Contain("Guardrail");
        Workers(all).First().Agent.Should().Be(AiAgentType.Writer);
        (await h.RunAsync(runId)).Status.Should().NotBe(AiRunStatus.Failed);
    }

    [Fact]
    public async Task ModelReturnsBrokenJsonOnce_IsRepairedOnce_AndStepRecordsTheRepair()
    {
        var h = await AiHarness.CreateAsync();
        var runId = await h.StartRunAsync();
        var mock = new MockChatClient();
        var brokenOnce = false;
        var chat = new ScriptedChatClient((_, msg) =>
        {
            if (msg.Contains("[[agent:writer]]") && !msg.Contains("[[repair]]") && !brokenOnce)
            {
                brokenOnce = true;
                return "{ không phải json";
            }
            return Ask(mock, msg);
        });

        await h.ExecuteWorkflowAsync(runId, chat);

        var writer = Workers(await h.StepsAsync(runId)).First();
        writer.RepairAttempts.Should().Be(1);
        writer.Status.Should().Be(AiStepStatus.Succeeded);
        chat.UserMessages.Should().Contain(m => m.Contains("[[repair]]") && m.Contains("[[agent:writer]]"));
    }

    [Fact]
    public async Task WriterStaysInvalid_AfterOneRepair_StepFailsWithClearError_RunFailed_NothingElseRuns()
    {
        var h = await AiHarness.CreateAsync();
        var runId = await h.StartRunAsync();
        var mock = new MockChatClient();
        var chat = new ScriptedChatClient((_, msg) => msg.Contains("[[agent:writer]]") ? "đây không phải JSON" : Ask(mock, msg));

        await h.ExecuteWorkflowAsync(runId, chat);

        chat.UserMessages.Count(m => m.Contains("[[agent:writer]]")).Should().Be(2, "một lần gọi + đúng một lần sửa");
        var steps = await h.StepsAsync(runId);
        steps.Select(s => s.Agent).Should().Equal(AiAgentType.Supervisor, AiAgentType.Writer);
        var step = steps.Last();
        step.Status.Should().Be(AiStepStatus.Failed);
        step.ErrorCode.Should().Be("INVALID_OUTPUT");
        step.RepairAttempts.Should().Be(1);
        var run = await h.RunAsync(runId);
        run.Status.Should().Be(AiRunStatus.Failed);
        run.ErrorCode.Should().Be("INVALID_OUTPUT");
    }

    [Fact]
    public async Task StoryboardBboxOutOfRange_IsRejectedByCode_AndRunFails()
    {
        var h = await AiHarness.CreateAsync();
        var runId = await h.StartRunAsync();
        var mock = new MockChatClient();
        var chat = new ScriptedChatClient((_, msg) =>
        {
            var text = Ask(mock, msg);
            // x + w = 1.4 > 1
            return msg.Contains("[[agent:storyboard]]") ? text.Replace("\"w\":1", "\"w\":1.4") : text;
        });

        await h.ExecuteWorkflowAsync(runId, chat);

        var steps = await h.StepsAsync(runId);
        steps.Last().Agent.Should().Be(AiAgentType.Storyboard);
        steps.Last().Status.Should().Be(AiStepStatus.Failed);
        steps.Last().ErrorMessage.Should().Contain("bbox");
        steps.Should().NotContain(s => s.Agent == AiAgentType.Editor);
        (await h.RunAsync(runId)).Status.Should().Be(AiRunStatus.Failed);
    }

    [Fact]
    public async Task ContinuityCitesLineOutsideScript_IsRejected()
    {
        var h = await AiHarness.CreateAsync();
        var runId = await h.StartRunAsync();
        var mock = new MockChatClient();
        var chat = new ScriptedChatClient((_, msg) =>
        {
            var text = Ask(mock, msg);
            return msg.Contains("[[agent:continuity]]") ? text.Replace("\"L4\"", "\"L99\"") : text;
        });

        await h.ExecuteWorkflowAsync(runId, chat);

        var failed = (await h.StepsAsync(runId)).Last();
        failed.Agent.Should().Be(AiAgentType.Continuity);
        failed.Status.Should().Be(AiStepStatus.Failed);
        failed.ErrorMessage.Should().Contain("L99");
    }

    [Fact]
    public async Task StoryboardCannotCreateLetteringElements()
    {
        var h = await AiHarness.CreateAsync();
        var runId = await h.StartRunAsync();
        var mock = new MockChatClient();
        var chat = new ScriptedChatClient((_, msg) =>
        {
            var text = Ask(mock, msg);
            return msg.Contains("[[agent:storyboard]]") ? text.Replace("\"camera\"", "\"elements\":[{\"type\":\"speech\"}],\"camera\"") : text;
        });

        await h.ExecuteWorkflowAsync(runId, chat);

        var failed = (await h.StepsAsync(runId)).Last();
        failed.Agent.Should().Be(AiAgentType.Storyboard);
        failed.ErrorMessage.Should().Contain("lettering");
    }
}
