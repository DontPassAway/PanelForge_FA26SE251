using FluentAssertions;
using PanelForge.AI.Contracts;
using Xunit;

namespace PanelForge.UnitTests.Ai;

/// <summary>
/// Hàng rào theo LOẠI workflow: chuỗi agent hợp lệ, điều kiện Finish riêng từng workflow (không phải "luôn sau Editor"),
/// thiếu context → StopForHuman, và Supervisor không bao giờ được chọn agent ngoài workflow.
/// </summary>
public class WorkflowTypeRulesTests
{
    private static StepFact Step(string agent, bool blocking = false, int round = 0) => new(agent, round, true, blocking);
    private static StepFact W(int round = 0) => Step("Writer", round: round);
    private static StepFact C(bool blocking = false, int round = 0) => Step("Continuity", blocking, round);

    private static IReadOnlyList<string> Allowed(string type, params StepFact[] steps) =>
        WorkflowRules.AllowedActions(type, WorkflowInputs.All, steps, 2);

    [Fact]
    public void Script_FinishesRightAfterCleanContinuity_WithoutStoryboardOrEditor()
    {
        Allowed("Script").Should().Equal("Writer");
        Allowed("Script", W()).Should().Equal("Continuity");
        Allowed("Script", W(), C()).Should().Equal("Finish");
    }

    [Fact]
    public void Script_KeepsTheTwoRoundRevisionLoop_AndNeverFinishesWithBlockingFindings()
    {
        Allowed("Script", W(), C(true)).Should().BeEquivalentTo(["Writer", "StopForHuman"]);
        Allowed("Script", W(), C(true), W(1), C(true, 1)).Should().BeEquivalentTo(["Writer", "StopForHuman"]);
        Allowed("Script", W(), C(true), W(1), C(true, 1), W(2), C(true, 2)).Should().Equal("StopForHuman");
    }

    [Fact]
    public void FullScene_StillEndsAfterEditor_OnlyAfterStoryboard()
    {
        Allowed("FullScene", W(), C()).Should().Equal("Storyboard");
        Allowed("FullScene", W(), C(), Step("Storyboard")).Should().Equal("Editor");
        Allowed("FullScene", W(), C(), Step("Storyboard"), Step("Editor")).Should().Equal("Finish");
        AiProtocolWorkflowsExceptFullScene().Should().NotContain(t => Allowed(t).Contains("Finish"));
    }

    [Fact]
    public void Storyboard_ChecksContinuityFirst_HasNoWriter_AndStopsOnBlocking()
    {
        Allowed("Storyboard").Should().Equal("Continuity");
        Allowed("Storyboard", C()).Should().Equal("Storyboard");
        Allowed("Storyboard", C(true)).Should().Equal("StopForHuman");
        Allowed("Storyboard", C(), Step("Storyboard")).Should().Equal("Editor");
        Allowed("Storyboard", C(), Step("Storyboard"), Step("Editor")).Should().Equal("Finish");
    }

    [Fact]
    public void Editor_ReviewsEvenWithBlockingFindings_ThenFinishes()
    {
        Allowed("Editor").Should().Equal("Continuity");
        Allowed("Editor", C(true)).Should().Equal("Editor");
        Allowed("Editor", C(), Step("Editor")).Should().Equal("Finish");
    }

    [Theory]
    [InlineData("Artist")]
    public void SingleAgentWorkflows_RunOnlyTheirOwnAgent_ThenFinish(string type)
    {
        Allowed(type).Should().Equal(type);
        Allowed(type, Step(type)).Should().Equal("Finish");
    }

    [Theory]
    [InlineData("Artist", new[] { "Writer", "Continuity", "Storyboard", "Editor", "Letterer", "Producer" })]
    [InlineData("Storyboard", new[] { "Writer", "Artist", "Letterer", "Producer" })]
    [InlineData("Editor", new[] { "Writer", "Storyboard", "Artist", "Letterer", "Producer" })]
    [InlineData("Script", new[] { "Storyboard", "Editor", "Artist", "Letterer", "Producer" })]
    public void AgentsOutsideTheWorkflow_AreNeverAllowed_AtAnyPoint(string type, string[] forbidden)
    {
        var steps = new List<StepFact>();
        for (var i = 0; i < 6; i++)
        {
            var allowed = Allowed(type, steps.ToArray());
            allowed.Should().NotIntersectWith(forbidden);
            if (allowed.Count == 0 || WorkflowRules.IsTerminal(allowed[0])) break;
            steps.Add(Step(allowed[0]));
        }
        WorkflowRules.AgentsOf(type).Should().NotIntersectWith(forbidden);
    }

    [Theory]
    [InlineData("Storyboard")]
    [InlineData("Editor")]
    public void MissingScript_OnlyStopForHuman(string type) =>
        WorkflowRules.AllowedActions(type, new WorkflowInputs(false, false, false), [], 2)
            .Should().Equal("StopForHuman");

    [Fact]
    public void MissingPanels_MissingLines_MissingProgress_OnlyStopForHuman()
    {
        var none = new WorkflowInputs(true, true, false);
        WorkflowRules.AllowedActions("Artist", none, [], 2).Should().Equal("StopForHuman");
        WorkflowRules.AllowedActions("Artist", new WorkflowInputs(false, false, true), [], 2).Should().Equal("Artist");
    }

    [Fact]
    public void WriterWorkflows_DoNotNeedExistingScript()
    {
        var none = new WorkflowInputs(false, false, false);
        WorkflowRules.AllowedActions("Script", none, [], 2).Should().Equal("Writer");
        WorkflowRules.AllowedActions("FullScene", none, [], 2).Should().Equal("Writer");
    }

    [Fact]
    public void UnknownWorkflowOrUnexpectedAgent_FallsBackToStopForHuman_NotToAnAgent()
    {
        Allowed("Nonsense").Should().Equal("StopForHuman");
        Allowed("Artist", Step("Writer")).Should().Equal("StopForHuman");
        Allowed("Script", Step("Producer")).Should().Equal("StopForHuman");
    }

    [Fact]
    public void FailedLastStep_AllowsNothing()
    {
        Allowed("Artist", new StepFact("Artist", 0, false, false)).Should().BeEmpty();
    }

    private static IEnumerable<string> AiProtocolWorkflowsExceptFullScene() =>
        AiProtocol.AllWorkflows.Where(t => t != "FullScene");
}
