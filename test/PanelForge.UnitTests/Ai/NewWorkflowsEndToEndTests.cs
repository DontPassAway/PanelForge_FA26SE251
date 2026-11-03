using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using PanelForge.AI.Contracts;
using PanelForge.AI.Demo;
using PanelForge.AI.Demo.Fixtures;
using PanelForge.Application.Common;
using PanelForge.Application.Features.AiAgents.Commands.RecordAgentStep;
using PanelForge.Application.Features.AiAgents.Models;
using PanelForge.Application.Features.AiAgents.Queries.GetAgentContext;
using PanelForge.Application.Features.AiAgents.Queries.GetRun;
using PanelForge.Domain.Entities.Ai;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Enums;
using PanelForge.Infrastructure.Persistence.Seeders;
using Xunit;

namespace PanelForge.UnitTests.Ai;

/// <summary>
/// Mock end-to-end cho từng loại workflow trên HAI phía thật: MultiAgentWorkflow (PanelForge.AI) → handler Backend. Mỗi workflow chỉ chạy
/// agent của nó, Finish đúng điều kiện của nó, lưu output/prompt version+hash/provenance, và chỉ tạo đề xuất áp dụng được khi đúng loại.
/// </summary>
public class NewWorkflowsEndToEndTests
{
    private static List<AiAgentType> Agents(IEnumerable<AiAgentStep> s) => s.Select(x => x.Agent).ToList();
    private static List<AiAgentStep> Workers(IEnumerable<AiAgentStep> s) => s.Where(x => x.Agent != AiAgentType.Supervisor).ToList();
    private static List<SupervisorDecision> Decisions(IEnumerable<AiAgentStep> s) =>
        s.Where(x => x.Agent == AiAgentType.Supervisor).Select(x => AiJson.Deserialize<SupervisorDecision>(x.OutputJson!)!).ToList();
    private static string Ask(IChatClient mock, string msg) => mock.GetResponseAsync([new ChatMessage(ChatRole.User, msg)]).Result.Text;

    /// <summary>Chạy Script (Mock) rồi Accept script: Scene có script đã qua Continuity + revision (chưa có layout).</summary>
    private static async Task AcceptScriptOnlyAsync(AiHarness h)
    {
        var runId = await h.RunMockAsync(AiWorkflowType.Script);
        var script = await h.SuggestionAsync(runId, AiSuggestionKind.Script);
        (await h.ReviewAsync(script.Id, AiSuggestionStatus.Accepted, AiHarness.Writer, chapterVersion: 0)).IsSuccess.Should().BeTrue();
    }

    // ── Script ───────────────────────────────────────────────────────────────
    [Fact]
    public async Task Script_WriterWrong_ContinuityCatches_WriterFixes_ThenFinish_WithoutStoryboardOrEditor()
    {
        var h = await AiHarness.CreateAsync();
        var runId = await h.RunMockAsync(AiWorkflowType.Script);

        var steps = await h.StepsAsync(runId);
        Agents(steps).Should().Equal(
            AiAgentType.Supervisor, AiAgentType.Writer, AiAgentType.Supervisor, AiAgentType.Continuity,
            AiAgentType.Supervisor, AiAgentType.Writer, AiAgentType.Supervisor, AiAgentType.Continuity, AiAgentType.Supervisor);
        Decisions(steps).Select(d => d.Action).Should().Equal("Writer", "Continuity", "Writer", "Continuity", "Finish");
        Decisions(steps).Should().OnlyContain(d => !d.GuardrailOverridden);

        var run = await h.RunAsync(runId);
        run.Status.Should().Be(AiRunStatus.Completed);
        run.WorkflowType.Should().Be(AiWorkflowType.Script);
        run.RevisionRounds.Should().Be(1);
        var suggestions = await h.Db.AiSuggestions.Where(s => s.RunId == runId).ToListAsync();
        suggestions.Should().ContainSingle().Which.Kind.Should().Be(AiSuggestionKind.Script);
        suggestions[0].HasBlockingFindings.Should().BeFalse();
        suggestions[0].ParentSuggestionId.Should().BeNull();
    }

    [Fact]
    public async Task Script_Stubborn_StopsForHuman_AfterTwoRounds_WithBlockingScriptSuggestion()
    {
        var h = await AiHarness.CreateAsync();
        var runId = await h.RunMockAsync(AiWorkflowType.Script, brief: $"Viết cảnh bà Lan nhìn An. {MockFixtures.StubbornMarker}");

        var steps = await h.StepsAsync(runId);
        Decisions(steps).Last().Action.Should().Be("StopForHuman");
        Workers(steps).Select(s => s.Agent).Should().Equal(
            AiAgentType.Writer, AiAgentType.Continuity, AiAgentType.Writer, AiAgentType.Continuity, AiAgentType.Writer, AiAgentType.Continuity);
        (await h.RunAsync(runId)).Status.Should().Be(AiRunStatus.WaitingForHuman);
        (await h.SuggestionAsync(runId, AiSuggestionKind.Script)).HasBlockingFindings.Should().BeTrue();
    }

    // ── Storyboard (script có sẵn) ───────────────────────────────────────────
    [Fact]
    public async Task Storyboard_UsesExistingScript_ContinuityFirst_ThenStoryboard_Editor_Finish_LayoutHasNoScriptParent()
    {
        var h = await AiHarness.CreateAsync();
        await AcceptScriptOnlyAsync(h);
        var scriptRevision = await h.Db.ContentRevisions.SingleAsync();

        var runId = await h.RunMockAsync(AiWorkflowType.Storyboard);

        var steps = await h.StepsAsync(runId);
        Agents(steps).Should().Equal(
            AiAgentType.Supervisor, AiAgentType.Continuity, AiAgentType.Supervisor, AiAgentType.Storyboard,
            AiAgentType.Supervisor, AiAgentType.Editor, AiAgentType.Supervisor);
        Decisions(steps).Select(d => d.Action).Should().Equal("Continuity", "Storyboard", "Editor", "Finish");
        var workers = Workers(steps);
        workers[0].ParentStepId.Should().BeNull("Continuity kiểm script có sẵn, không có Writer trong run");
        workers[1].ParentStepId.Should().Be(workers[0].Id);
        workers[2].ParentStepId.Should().Be(workers[1].Id);
        steps.Should().NotContain(s => s.Agent == AiAgentType.Writer);

        var layoutSuggestion = await h.SuggestionAsync(runId, AiSuggestionKind.Layout);
        layoutSuggestion.ParentSuggestionId.Should().BeNull();
        layoutSuggestion.BaseScriptRevisionId.Should().Be(scriptRevision.Id);
        h.Db.AiSuggestions.Count(s => s.RunId == runId).Should().Be(1, "không có Script suggestion vì script không do run này viết");
        (await h.RunAsync(runId)).Status.Should().Be(AiRunStatus.Completed);

        // Accept: layout dựa trên script có sẵn đúng revision → tạo Page/Panel, chương tăng version
        var accept = await h.ReviewAsync(layoutSuggestion.Id, AiSuggestionStatus.Accepted, AiHarness.Writer, chapterVersion: 1);
        accept.IsSuccess.Should().BeTrue(accept.ErrorMessage);
        accept.Value!.ChapterVersion.Should().Be(2);
        h.Db.Pages.Count().Should().Be(1);
        h.Db.Panels.Count().Should().BeGreaterThan(0);
        (await h.Db.ContentRevisions.SingleAsync(r => r.Kind == ContentRevisionKind.Layout)).SourceSuggestionId.Should().Be(layoutSuggestion.Id);
    }

    [Fact]
    public async Task Storyboard_ScriptWithBlockingContinuityFinding_StopsForHuman_NoWriterNoStoryboardNoSuggestion()
    {
        var h = await AiHarness.CreateAsync();
        var an = (await h.Db.BibleEntries.SingleAsync(e => e.Code == "AN")).Id;
        h.Db.ScriptLines.Add(ScriptLine.Create(AiDemoSeeder.SceneId, 1, "Cháu có chìa khóa bạc đây, bà thử xem.", null, an));
        await h.Db.SaveChangesAsync();

        var runId = await h.RunMockAsync(AiWorkflowType.Storyboard, AiHarness.Writer);

        var steps = await h.StepsAsync(runId);
        Agents(steps).Should().Equal(AiAgentType.Supervisor, AiAgentType.Continuity, AiAgentType.Supervisor);
        var last = Decisions(steps).Last();
        last.Action.Should().Be("StopForHuman");
        last.AllowedActions.Should().Equal("StopForHuman");
        last.Reason.Should().Contain("không có Writer");
        (await h.RunAsync(runId)).Status.Should().Be(AiRunStatus.WaitingForHuman);
        h.Db.AiSuggestions.Count(s => s.RunId == runId).Should().Be(0);

        // Continuity trên script có sẵn đồng bộ finding vào ConsistencyFindings để Editor triage (BR-21)
        var finding = await h.Db.ConsistencyFindings.SingleAsync(f => f.ChapterId == AiDemoSeeder.ChapterId);
        finding.RuleCode.Should().Be("AI_CONTINUITY_ITEMTIMELINE");
        finding.Severity.Should().Be(ConsistencyFindingSeverity.Blocking);
        finding.Status.Should().Be(ConsistencyFindingStatus.Open);
        finding.Evidence.Should().Contain("Chương 5");
    }

    [Fact]
    public async Task Storyboard_WhenContextIsMissingAtRunTime_CodeStopsForHuman_WithoutCallingTheModel()
    {
        var h = await AiHarness.CreateAsync();
        await AcceptScriptOnlyAsync(h);
        var runId = await h.StartAsync(AiWorkflowType.Storyboard);
        // Mô phỏng snapshot thiếu script (ví dụ dữ liệu hỏng): hàng rào thấy thiếu input → chỉ còn StopForHuman
        var run = await h.RunAsync(runId);
        var snap = JsonNode.Parse(run.ContextSnapshotJson)!.AsObject();
        snap["baselineScript"] = new JsonArray();
        typeof(AiWorkflowRun).GetProperty(nameof(AiWorkflowRun.ContextSnapshotJson))!.SetValue(run, snap.ToJsonString());
        await h.Db.SaveChangesAsync();
        var chat = new ScriptedChatClient((_, _) => throw new InvalidOperationException("Không được gọi model khi thiếu context."));

        await h.ExecuteWorkflowAsync(runId, chat);

        chat.Calls.Should().Be(0);
        var steps = await h.StepsAsync(runId);
        steps.Should().ContainSingle().Which.Agent.Should().Be(AiAgentType.Supervisor);
        var decision = Decisions(steps).Single();
        decision.Action.Should().Be("StopForHuman");
        decision.Reason.Should().Contain("Thiếu context").And.Contain("script");
        (await h.RunAsync(runId)).Status.Should().Be(AiRunStatus.WaitingForHuman);
        h.Db.AiSuggestions.Count(s => s.RunId == runId).Should().Be(0);
    }

    // ── Editor (báo cáo tham khảo) ───────────────────────────────────────────
    [Fact]
    public async Task Editor_WithoutLayout_ReviewsScriptOnly_NoSuggestion_ChecklistSaysNoLayout()
    {
        var h = await AiHarness.CreateAsync();
        await AcceptScriptOnlyAsync(h);

        var runId = await h.RunMockAsync(AiWorkflowType.Editor);

        var steps = await h.StepsAsync(runId);
        Agents(steps).Should().Equal(
            AiAgentType.Supervisor, AiAgentType.Continuity, AiAgentType.Supervisor, AiAgentType.Editor, AiAgentType.Supervisor);
        var report = AiJson.Deserialize<ReviewReport>(Workers(steps)[1].OutputJson!)!;
        report.Checklist.Should().Contain(c => c.Name.Contains("layout") && !c.Passed);
        (await h.RunAsync(runId)).Status.Should().Be(AiRunStatus.Completed);
        h.Db.AiSuggestions.Count(s => s.RunId == runId).Should().Be(0, "báo cáo review chỉ để tham khảo, không có gì để Accept");
    }

    [Fact]
    public async Task Editor_WithApprovedLayout_IncludesLayoutChecksInTheReport()
    {
        var h = await AiHarness.CreateAsync();
        await h.AcceptScriptAndLayoutAsync();

        var runId = await h.RunMockAsync(AiWorkflowType.Editor);

        var report = AiJson.Deserialize<ReviewReport>(Workers(await h.StepsAsync(runId))[1].OutputJson!)!;
        report.Checklist.Should().Contain(c => c.Name.Contains("Bbox") && c.Passed);
        report.Checklist.Should().Contain(c => c.Name.Contains("Thứ tự đọc") && c.Passed);
        h.Db.AiSuggestions.Count(s => s.RunId == runId).Should().Be(0);
    }

    // ── Artist ───────────────────────────────────────────────────────────────
    [Fact]
    public async Task Artist_WritesOnePromptPerPanel_WithReferences_Provenance_AndAPendingImagePromptSuggestion()
    {
        var h = await AiHarness.CreateAsync();
        await h.AcceptScriptAndLayoutAsync();
        var bibleRevisionsBefore = h.Db.BibleEntryRevisions.Count();

        var runId = await h.RunMockAsync(AiWorkflowType.Artist);

        var steps = await h.StepsAsync(runId);
        Agents(steps).Should().Equal(AiAgentType.Supervisor, AiAgentType.Artist, AiAgentType.Supervisor);
        steps.Select(s => s.Agent).Should().NotContain([AiAgentType.Writer, AiAgentType.Continuity, AiAgentType.Storyboard, AiAgentType.Editor],
            "không ép cả 7 agent chạy trong mọi run");
        var artistStep = steps[1];
        artistStep.PromptVersion.Should().Be("artist@1");
        artistStep.PromptRef.Should().StartWith("Prompts/artist.md@v1#sha256:");
        artistStep.ParametersJson.Should().Contain("temperature");
        artistStep.PromptTokens.Should().BeNull("Mock không khai token");

        var set = AiJson.Deserialize<ArtistPromptSet>(artistStep.OutputJson!)!;
        var panels = await h.Db.Panels.ToListAsync();
        set.Panels.Select(p => p.PanelId).Should().BeEquivalentTo(panels.Select(p => p.Id), "mỗi panel đúng một prompt");
        set.Panels.Should().OnlyContain(p => p.Prompt.Length > 0 && p.ReferenceIds.Count > 0);
        var run = await h.RunAsync(runId);
        var bibleIds = (await h.Db.BibleEntries.ToListAsync()).Select(b => b.Id).ToHashSet();
        set.Panels.SelectMany(p => p.ReferenceIds).Should().OnlyContain(id => bibleIds.Contains(id), "không có ID ngoài Bible của context");
        set.LayoutRevisionId.Should().Be((await h.Db.ContentRevisions.SingleAsync(r => r.Kind == ContentRevisionKind.Layout)).Id);

        run.Status.Should().Be(AiRunStatus.Completed);
        var suggestion = await h.Db.AiSuggestions.SingleAsync(s => s.RunId == runId);
        suggestion.Kind.Should().Be(AiSuggestionKind.ImagePrompt);
        suggestion.Status.Should().Be(AiSuggestionStatus.Pending);
        suggestion.PayloadHash.Should().NotBeNullOrEmpty();
        h.Db.BibleEntryRevisions.Count().Should().Be(bibleRevisionsBefore, "Artist không tự sửa Bible");
    }

    // ── Output bịa / sai bị code chặn (sau đúng một lần sửa) ──────────────────
    private static async Task<(AiHarness H, Guid RunId)> RunTamperedAsync(
        AiWorkflowType type, string agentMarker, Func<string, string> tamper, bool prepareLayout = true)
    {
        var h = await AiHarness.CreateAsync();
        if (prepareLayout) await h.AcceptScriptAndLayoutAsync();
        var runId = await h.StartAsync(type);
        var mock = new MockChatClient();
        var chat = new ScriptedChatClient((_, msg) => msg.Contains(agentMarker) ? tamper(Ask(mock, msg)) : Ask(mock, msg));
        await h.ExecuteWorkflowAsync(runId, chat);
        return (h, runId);
    }

    [Fact]
    public async Task Artist_ReferenceOutsideBible_IsRejected_AndNeverReachesASuggestion()
    {
        var (h, runId) = await RunTamperedAsync(AiWorkflowType.Artist, "[[agent:artist]]",
            json => json.Replace("\"referenceKeys\":[", "\"referenceKeys\":[\"B999\","));

        var failed = (await h.StepsAsync(runId)).Last();
        failed.Agent.Should().Be(AiAgentType.Artist);
        failed.Status.Should().Be(AiStepStatus.Failed);
        failed.ErrorMessage.Should().Contain("B999");
        h.Db.AiSuggestions.Count(s => s.RunId == runId).Should().Be(0);
    }

    [Fact]
    public async Task Artist_PanelWithoutItsCharacterReference_IsRejected()
    {
        var (h, runId) = await RunTamperedAsync(AiWorkflowType.Artist, "[[agent:artist]]",
            json => System.Text.RegularExpressions.Regex.Replace(json, "\"referenceKeys\":\\[[^\\]]*\\]", "\"referenceKeys\":[]"));

        var failed = (await h.StepsAsync(runId)).Last();
        failed.Status.Should().Be(AiStepStatus.Failed);
        failed.ErrorMessage.Should().Contain("thiếu tham chiếu nhân vật");
    }

    // ── Supervisor chọn agent không được phép ────────────────────────────────
    [Theory]
    [InlineData(AiWorkflowType.Artist, "Artist")]
    public async Task SingleAgentWorkflow_NeverAsksTheSupervisorModel_SoItCannotBeSteeredOutsideTheWorkflow(AiWorkflowType type, string expectedAgent)
    {
        var h = await AiHarness.CreateAsync();
        await h.AcceptScriptAndLayoutAsync();
        var runId = await h.StartAsync(type);
        var mock = new MockChatClient();
        // Nếu được hỏi, Supervisor sẽ đòi chạy Writer (không thuộc workflow); mọi bước chỉ có một lựa chọn nên code quyết định, model không được hỏi
        var chat = new ScriptedChatClient((_, msg) => msg.Contains("[[agent:supervisor]]")
            ? "{\"action\":\"Writer\",\"reason\":\"viết lại script\",\"instruction\":null}"
            : Ask(mock, msg));

        await h.ExecuteWorkflowAsync(runId, chat);

        chat.UserMessages.Should().NotContain(m => m.Contains("[[agent:supervisor]]"));
        var all = await h.StepsAsync(runId);
        var first = AiJson.Deserialize<SupervisorDecision>(all[0].OutputJson!)!;
        first.GuardrailOverridden.Should().BeFalse();
        first.Action.Should().Be(expectedAgent);
        first.AllowedActions.Should().Equal(expectedAgent);
        all[0].ParametersJson.Should().Contain("single-allowed-action");
        all.Should().NotContain(s => s.Agent == AiAgentType.Writer);
        (await h.RunAsync(runId)).Status.Should().Be(AiRunStatus.Completed);
    }

    private static AgentStepReport SupervisorStep(Guid runId, int index, string action, IReadOnlyList<string> allowed, int? parent = null)
    {
        var decision = new SupervisorDecision(AiProtocol.SchemaVersion, runId, 0, action, "Lý do", null, allowed, false);
        var now = DateTime.UtcNow;
        return new AgentStepReport(index, "Supervisor", 0, parent, true, "Mock", "mock-fixture-v1", "supervisor@2", "Prompts/supervisor.md@v2#sha256:x",
            "{}", "{}", AiJson.Serialize(decision), now, now, null, null, 0, null, null);
    }

    [Theory]
    [InlineData(AiWorkflowType.Artist, "Finish")]        // chưa có Artist đã chạy mà Finish
    [InlineData(AiWorkflowType.Artist, "Writer")]        // agent ngoài workflow
    public async Task Backend_RejectsSupervisorDecisionsOutsideTheAllowedSet_ForTheRunsWorkflow(AiWorkflowType type, string action)
    {
        var h = await AiHarness.CreateAsync();
        await h.AcceptScriptAndLayoutAsync();
        var runId = await h.StartAsync(type);

        var result = await h.RecordHandler().Handle(new RecordAgentStepCommand(runId, SupervisorStep(runId, 1, action, [action])), default);

        result.ErrorCode.Should().Be(ResultErrorCodes.Validation);
        result.ErrorMessage.Should().Contain("không nằm trong danh sách được phép");
        h.Db.AiAgentSteps.Count(s => s.RunId == runId).Should().Be(0);
    }

    [Fact]
    public async Task Backend_RejectsAnAgentStepThatTheSupervisorDidNotChoose_AndOutOfOrderSteps()
    {
        var h = await AiHarness.CreateAsync();
        await h.AcceptScriptAndLayoutAsync();
        var runId = await h.StartAsync(AiWorkflowType.Artist);
        (await h.RecordHandler().Handle(new RecordAgentStepCommand(runId, SupervisorStep(runId, 1, "Artist", ["Artist"])), default))
            .IsSuccess.Should().BeTrue();

        var now = DateTime.UtcNow;
        var writer = new AgentStepReport(2, "Writer", 0, null, true, "Mock", "mock-fixture-v1", "writer@1", "ref", "{}", "{}", "{}", now, now, null, null, 0, null, null);
        var result = await h.RecordHandler().Handle(new RecordAgentStepCommand(runId, writer), default);

        result.ErrorCode.Should().Be(ResultErrorCodes.Validation);
        result.ErrorMessage.Should().Contain("Supervisor đã chọn 'Artist'");
        h.Db.AiAgentSteps.Count(s => s.RunId == runId).Should().Be(1);
    }

    // ── Run cũ vẫn đọc và chạy được ──────────────────────────────────────────
    [Fact]
    public async Task LegacyRun_WithoutNewSnapshotFieldsOrWorkflowType_StillReadsAndRuns_AsFullScene()
    {
        var h = await AiHarness.CreateAsync();
        var runId = await h.StartAsync(AiWorkflowType.FullScene);
        var run = await h.RunAsync(runId);
        // Snapshot đúng dạng của build trước khi có workflow type (chỉ 5 trường)
        var snap = JsonNode.Parse(run.ContextSnapshotJson)!.AsObject();
        foreach (var extra in new[] { "existingLayout", "layoutRevisionId", "artist", "lettering", "progress" }) snap.Remove(extra);
        typeof(AiWorkflowRun).GetProperty(nameof(AiWorkflowRun.ContextSnapshotJson))!.SetValue(run, snap.ToJsonString());
        await h.Db.SaveChangesAsync();

        var ctx = (await new GetAgentContextQueryHandler(h.Db).Handle(new GetAgentContextQuery(runId), default)).Value!;
        ctx.WorkflowType.Should().Be("FullScene");
        (ctx.ExistingLayout, ctx.Artist).Should().Be((null, null));

        await h.ExecuteWorkflowAsync(runId);

        (await h.RunAsync(runId)).Status.Should().Be(AiRunStatus.Completed);
        Agents(await h.StepsAsync(runId)).Count.Should().Be(13, "chuỗi 13 step của Hướng B không đổi");
        var dto = (await new GetRunQueryHandler(h.Db, h.Auth).Handle(new GetRunQuery(AiDemoSeeder.SeriesId, runId, AiHarness.Editor), default)).Value!;
        dto.WorkflowType.Should().Be(AiWorkflowType.FullScene);
        dto.SceneId.Should().Be(AiDemoSeeder.SceneId);
        dto.Suggestions.Should().HaveCount(2);
    }

    [Fact]
    public void LegacyAgentContextJson_WithoutNewFields_DeserializesWithDefaults()
    {
        var json = """
        {"runId":"a1000000-0000-0000-0000-0000000000a1","seriesId":"a1000000-0000-0000-0000-000000000002","chapterId":"a1000000-0000-0000-0000-000000000003",
         "chapterNumber":3,"sceneId":"a1000000-0000-0000-0000-000000000004","sceneHeading":"H","sceneSummary":null,"brief":"b","readingDirection":"RightToLeft",
         "chapterVersion":0,"provider":"Mock","model":"mock-fixture-v1","maxRevisionRounds":2,"bible":[],"bibleRevisionIds":[],"baselineScript":[]}
        """;

        var ctx = AiJson.Deserialize<AgentContext>(json)!;

        ctx.WorkflowType.Should().Be("FullScene");
        ctx.Inputs.Should().Be(new WorkflowInputs(false, false, false));
        WorkflowRules.AllowedActions(ctx.WorkflowType, ctx.Inputs, [], 2).Should().Equal("Writer");
    }

    [Fact]
    public void LegacyHandlers_StillWork_DemoRouteDefaultsToFullScene()
    {
        new RunWorkflowRequest(Guid.NewGuid(), Guid.NewGuid(), "brief", ReadingDirection.RightToLeft, 0).WorkflowType
            .Should().Be(AiWorkflowType.FullScene);
    }
}
