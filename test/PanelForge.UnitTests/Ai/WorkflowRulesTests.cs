using FluentAssertions;
using PanelForge.AI.Contracts;
using Xunit;

namespace PanelForge.UnitTests.Ai;

/// <summary>Hàng rào cứng của workflow (một nguồn sự thật cho AI service và Backend): Supervisor chỉ được chọn trong danh sách này.</summary>
public class WorkflowRulesTests
{
    private static StepFact W(int round = 0) => new("Writer", round, true, false);
    private static StepFact C(bool blocking, int round = 0) => new("Continuity", round, true, blocking);
    private static StepFact S() => new("Storyboard", 0, true, false);
    private static StepFact E() => new("Editor", 0, true, false);

    [Fact]
    public void Start_OnlyWriter()
    {
        WorkflowRules.AllowedActions([], 2).Should().Equal("Writer");
    }

    [Fact]
    public void AfterWriter_OnlyContinuity_NoSkippingTheCheck()
    {
        WorkflowRules.AllowedActions([W()], 2).Should().Equal("Continuity");
    }

    [Fact]
    public void AfterContinuityWithBlocking_RevisionOrStop_WhileRoundsRemain()
    {
        WorkflowRules.AllowedActions([W(), C(true)], 2).Should().BeEquivalentTo(["Writer", "StopForHuman"]);
        WorkflowRules.AllowedActions([W(), C(true), W(1), C(true, 1)], 2).Should().BeEquivalentTo(["Writer", "StopForHuman"]);
    }

    [Fact]
    public void AfterContinuityWithBlocking_AtMaxRounds_OnlyStopForHuman_NeverStoryboard()
    {
        WorkflowRules.AllowedActions([W(), C(true), W(1), C(true, 1), W(2), C(true, 2)], 2).Should().Equal("StopForHuman");
        WorkflowRules.AllowedActions([W(), C(true)], 0).Should().Equal("StopForHuman");
    }

    [Fact]
    public void AfterCleanContinuity_StoryboardOnly_CannotStopOrFinishEarly()
    {
        WorkflowRules.AllowedActions([W(), C(false)], 2).Should().Equal("Storyboard");
        WorkflowRules.AllowedActions([W(), C(true), W(1), C(false, 1)], 2).Should().Equal("Storyboard");
    }

    [Fact]
    public void Pipeline_Continues_StoryboardThenEditorThenFinish()
    {
        WorkflowRules.AllowedActions([W(), C(false), S()], 2).Should().Equal("Editor");
        WorkflowRules.AllowedActions([W(), C(false), S(), E()], 2).Should().Equal("Finish");
    }

    [Fact]
    public void AfterFailedStep_NothingIsAllowed()
    {
        WorkflowRules.AllowedActions([W(), new StepFact("Continuity", 0, false, false)], 2).Should().BeEmpty();
    }

    [Fact]
    public void ValidateSupervisor_RejectsOutsideActions_EmptyReason_AndOverlongInstruction()
    {
        var runId = Guid.NewGuid();
        SupervisorDecision D(string action, string reason = "lý do", string? instruction = null) =>
            new(1, runId, 0, action, reason, instruction, ["Writer"], false);

        ContentRules.ValidateSupervisor(D("Writer"), runId, ["Writer"]).Should().BeEmpty();
        ContentRules.ValidateSupervisor(D("Editor"), runId, ["Writer"]).Should().Contain(e => e.Contains("không nằm trong danh sách"));
        ContentRules.ValidateSupervisor(D("Writer", reason: " "), runId, ["Writer"]).Should().Contain(e => e.Contains("reason"));
        ContentRules.ValidateSupervisor(D("Writer", instruction: new string('x', 601)), runId, ["Writer"]).Should().Contain(e => e.Contains("instruction"));
        ContentRules.ValidateSupervisor(D("Writer") with { RunId = Guid.NewGuid() }, runId, ["Writer"]).Should().Contain(e => e.Contains("runId"));
    }
}
