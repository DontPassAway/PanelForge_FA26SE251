using FluentAssertions;
using Moq;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.AiAgents.Models;
using PanelForge.Application.Features.AiAgents.Queries.GetWorkflowAvailability;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Enums;
using PanelForge.Infrastructure.Persistence.Seeders;
using Xunit;

namespace PanelForge.UnitTests.Ai;

/// <summary>
/// Định tuyến nhiệm vụ theo vai trò / scope / stage / assignment / khoá chương / context. Quyền do Backend tự xác định
/// (user + membership + assignment), request không mang role hay danh sách agent; availability dùng cùng bộ luật với RunWorkflow.
/// </summary>
public class WorkflowRoutingTests
{
    private static Guid UserOf(string name) => name switch
    {
        "Producer" => AiHarness.Producer, "Writer" => AiHarness.Writer, "Artist" => AiHarness.Artist,
        "Letterer" => AiHarness.Letterer, "Editor" => AiHarness.Editor, _ => AiHarness.Reader
    };

    private static readonly string[] AllUsers = ["Producer", "Writer", "Artist", "Letterer", "Editor", "Reader"];
    private static readonly string[] Members = ["Producer", "Writer", "Artist", "Letterer", "Editor"];

    /// <summary>(workflow, người dùng, kết quả mong đợi trên Scene mới seed: Forbidden | Conflict(thiếu context) | Ok).</summary>
    public static IEnumerable<object[]> Matrix()
    {
        string[] RolesOf(AiWorkflowType t) => t switch
        {
            AiWorkflowType.FullScene or AiWorkflowType.Script => ["Producer", "Writer"],
            AiWorkflowType.Storyboard => ["Producer", "Writer", "Artist"],
            AiWorkflowType.Artist => ["Artist"],
            AiWorkflowType.Letterer => ["Letterer"],
            AiWorkflowType.Editor => ["Producer", "Editor"],
            _ => ["Producer"]
        };
        foreach (var type in Enum.GetValues<AiWorkflowType>())
            foreach (var user in AllUsers)
            {
                var allowedRole = RolesOf(type).Contains(user);
                var expected = !allowedRole ? "Forbidden"
                    : type is AiWorkflowType.FullScene or AiWorkflowType.Script or AiWorkflowType.Producer ? "Ok" : "Conflict";
                yield return [type, user, expected];
            }
    }

    [Theory]
    [MemberData(nameof(Matrix))]
    public async Task Run_RoleMatrix_IsDecidedByBackend_ProducerHasNoImplicitContentRights(AiWorkflowType type, string user, string expected)
    {
        var h = await AiHarness.CreateAsync();

        var result = await h.RunHandler().Handle(await h.CommandAsync(type, UserOf(user)), default);

        switch (expected)
        {
            case "Ok": result.IsSuccess.Should().BeTrue(result.ErrorMessage); break;
            case "Forbidden": result.ErrorCode.Should().Be(ResultErrorCodes.Forbidden); break;
            default: result.ErrorCode.Should().Be(ResultErrorCodes.Conflict, "đúng vai trò nhưng Scene chưa có script/layout/thoại"); break;
        }
        h.Db.AiWorkflowRuns.Count().Should().Be(result.IsSuccess ? 1 : 0, "bị chặn thì không tạo run và không gọi AI service");
        if (!result.IsSuccess) h.AiClient.Verify(c => c.StartRunAsync(It.IsAny<PanelForge.AI.Contracts.AiRunJob>(), default), Times.Never);
    }

    [Fact]
    public async Task Run_Artist_ByProducerOrWriter_IsForbidden_EvenWithCompleteContext()
    {
        var h = await AiHarness.CreateAsync();
        await h.AcceptScriptAndLayoutAsync();

        foreach (var user in new[] { AiHarness.Producer, AiHarness.Writer, AiHarness.Editor, AiHarness.Letterer })
            (await h.RunHandler().Handle(await h.CommandAsync(AiWorkflowType.Artist, user), default))
                .ErrorCode.Should().Be(ResultErrorCodes.Forbidden);
        (await h.RunHandler().Handle(await h.CommandAsync(AiWorkflowType.Artist, AiHarness.Artist), default))
            .IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Run_NotAMember_OrUnknownSeries_OrWrongScope_IsRejected()
    {
        var h = await AiHarness.CreateAsync();
        var cmd = await h.CommandAsync(AiWorkflowType.Script);

        (await h.RunHandler().Handle(cmd with { SeriesId = Guid.NewGuid() }, default)).ErrorCode.Should().Be(ResultErrorCodes.NotFound);
        (await h.RunHandler().Handle(cmd with { ChapterId = Guid.NewGuid() }, default)).ErrorCode.Should().Be(ResultErrorCodes.NotFound);

        // Scene của Chapter khác trong cùng Series không dùng được với Chapter trên request (BR-03)
        var otherChapter = Chapter.Create(AiDemoSeeder.SeriesId, 4, "Chương khác");
        h.Db.Chapters.Add(otherChapter);
        var otherScene = Scene.Create(otherChapter.Id, 1, "Cảnh chương khác", "x");
        h.Db.Scenes.Add(otherScene);
        await h.Db.SaveChangesAsync();
        (await h.RunHandler().Handle(cmd with { SceneId = otherScene.Id }, default)).ErrorCode.Should().Be(ResultErrorCodes.NotFound);

        // Không có sceneId với workflow nội dung → 400; Producer không cần Scene
        (await h.RunHandler().Handle(cmd with { SceneId = null }, default)).ErrorCode.Should().Be(ResultErrorCodes.Validation);
        (await h.RunHandler().Handle(cmd with { WorkflowType = (AiWorkflowType)99 }, default)).ErrorCode.Should().Be(ResultErrorCodes.Validation);
    }

    [Fact]
    public async Task Run_Producer_IsChapterScoped_NoScene_NoBibleNeeded_AndSnapshotHasProgress()
    {
        var h = await AiHarness.CreateAsync();
        var runId = await h.StartAsync(AiWorkflowType.Producer);

        var run = await h.RunAsync(runId);
        run.WorkflowType.Should().Be(AiWorkflowType.Producer);
        run.SceneId.Should().BeNull();
        var snap = PanelForge.AI.Contracts.AiJson.Deserialize<RunSnapshot>(run.ContextSnapshotJson)!;
        snap.Progress.Should().NotBeNull();
        snap.Progress!.Facts.Should().NotBeEmpty();
        snap.Progress.DataGaps.Should().Contain(g => g.Contains("assignment"), "Chapter chưa có assignment nào: phải nói rõ thiếu dữ liệu");
    }

    [Fact]
    public async Task Run_OnLockedChapter_ContentWorkflowsBlocked_ButReportsStillAllowed()
    {
        var h = await AiHarness.CreateAsync();
        await LockChapterAsync(h);

        var script = await h.RunHandler().Handle(await h.CommandAsync(AiWorkflowType.Script), default);
        script.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        script.ErrorMessage.Should().Contain("Locked");
        (await h.RunHandler().Handle(await h.CommandAsync(AiWorkflowType.Producer), default)).IsSuccess
            .Should().BeTrue("báo cáo tiến độ chỉ đọc nên không cần chương còn InProduction");
    }

    [Fact]
    public async Task Run_Assignment_GivenToSomeoneElse_BlocksTheRole_ButNotTheProducer()
    {
        var h = await AiHarness.CreateAsync();
        await AssignAsync(h, AiHarness.Editor, WorkspaceRole.Writer);   // việc Writer đã giao cho người khác

        var blocked = await h.RunHandler().Handle(await h.CommandAsync(AiWorkflowType.Script, AiHarness.Writer), default);

        blocked.ErrorCode.Should().Be(ResultErrorCodes.Forbidden);
        blocked.ErrorMessage.Should().Contain("giao việc");
        (await h.RunHandler().Handle(await h.CommandAsync(AiWorkflowType.Script, AiHarness.Producer), default)).IsSuccess
            .Should().BeTrue();
    }

    [Theory]
    [InlineData(AssignmentStatus.Assigned, true)]
    [InlineData(AssignmentStatus.InProgress, true)]
    [InlineData(AssignmentStatus.ReworkRequired, true)]
    [InlineData(AssignmentStatus.Submitted, false)]
    [InlineData(AssignmentStatus.Approved, false)]
    public async Task Run_Assignment_MustBeInWorkingState(AssignmentStatus status, bool allowed)
    {
        var h = await AiHarness.CreateAsync();
        await AssignAsync(h, AiHarness.Writer, WorkspaceRole.Writer, status);

        var result = await h.RunHandler().Handle(await h.CommandAsync(AiWorkflowType.Script, AiHarness.Writer), default);

        result.IsSuccess.Should().Be(allowed, result.ErrorMessage);
        if (!allowed) result.ErrorCode.Should().Be(ResultErrorCodes.Forbidden);
    }

    [Fact]
    public async Task Run_Assignment_StageMustBelongToTheRole()
    {
        var h = await AiHarness.CreateAsync();
        var artistStage = PipelineStage.Create(Guid.NewGuid(), "Pencil", "pencil", 2, allowedRole: WorkspaceRole.Artist);
        h.Db.PipelineStages.Add(artistStage);
        await h.Db.SaveChangesAsync();
        await AssignAsync(h, AiHarness.Writer, WorkspaceRole.Writer, stageId: artistStage.Id);   // Writer đứng ở stage dành cho Artist

        var result = await h.RunHandler().Handle(await h.CommandAsync(AiWorkflowType.Script, AiHarness.Writer), default);

        result.ErrorCode.Should().Be(ResultErrorCodes.Forbidden);
        result.ErrorMessage.Should().Contain("Stage");
    }

    [Fact]
    public async Task Run_Artist_RequiresOwnAssignmentWhenChapterHasArtistAssignments()
    {
        var h = await AiHarness.CreateAsync();
        await h.AcceptScriptAndLayoutAsync();
        await AssignAsync(h, AiHarness.Producer /* người khác */, WorkspaceRole.Artist);

        (await h.RunHandler().Handle(await h.CommandAsync(AiWorkflowType.Artist), default)).ErrorCode.Should().Be(ResultErrorCodes.Forbidden);

        await AssignAsync(h, AiHarness.Artist, WorkspaceRole.Artist, AssignmentStatus.InProgress);
        (await h.RunHandler().Handle(await h.CommandAsync(AiWorkflowType.Artist), default)).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Run_MissingContext_IsReportedPerWorkflow_AndFixedByEarlierSteps()
    {
        var h = await AiHarness.CreateAsync();

        (await h.RunHandler().Handle(await h.CommandAsync(AiWorkflowType.Storyboard), default)).ErrorMessage.Should().Contain("script");
        (await h.RunHandler().Handle(await h.CommandAsync(AiWorkflowType.Editor), default)).ErrorMessage.Should().Contain("script");
        (await h.RunHandler().Handle(await h.CommandAsync(AiWorkflowType.Artist), default)).ErrorMessage.Should().Contain("layout");
        (await h.RunHandler().Handle(await h.CommandAsync(AiWorkflowType.Letterer), default)).ErrorMessage.Should().Contain("thoại");

        await h.AcceptScriptAndLayoutAsync();
        (await h.RunHandler().Handle(await h.CommandAsync(AiWorkflowType.Artist), default)).IsSuccess.Should().BeTrue();
        (await h.RunHandler().Handle(await h.CommandAsync(AiWorkflowType.Letterer), default)).IsSuccess.Should().BeTrue();
        (await h.RunHandler().Handle(await h.CommandAsync(AiWorkflowType.Editor), default)).IsSuccess.Should().BeTrue();
        // Scene đã có Page do layout đã Accept → Storyboard không còn áp dụng được
        var storyboard = await h.RunHandler().Handle(await h.CommandAsync(AiWorkflowType.Storyboard), default);
        storyboard.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        storyboard.ErrorMessage.Should().Contain("Page");
    }

    [Fact]
    public async Task Run_StaleChapterVersionOrScriptRevision_StillConflicts_ForEveryContentWorkflow()
    {
        var h = await AiHarness.CreateAsync();
        await h.AcceptScriptAndLayoutAsync();

        foreach (var type in new[] { AiWorkflowType.Artist, AiWorkflowType.Letterer, AiWorkflowType.Editor })
        {
            var cmd = await h.CommandAsync(type);
            (await h.RunHandler().Handle(cmd with { ChapterVersion = cmd.ChapterVersion - 1 }, default)).ErrorCode
                .Should().Be(ResultErrorCodes.Conflict);
            (await h.RunHandler().Handle(cmd with { BaseScriptRevisionId = Guid.NewGuid() }, default)).ErrorCode
                .Should().Be(ResultErrorCodes.Conflict);
        }
    }

    [Fact]
    public async Task Run_AiDisabled_StillReportsAiDisabled()
    {
        var h = await AiHarness.CreateAsync();
        (await h.Db.WorkspaceAiConfigs.SingleAsync()).UpdateSettings("Google Gemini", "ciphertext", false, 1_000_000, "[]");
        await h.Db.SaveChangesAsync();

        (await h.RunHandler().Handle(await h.CommandAsync(AiWorkflowType.Script), default)).ErrorCode.Should().Be(ResultErrorCodes.AiDisabled);
    }

    // ── API availability: cùng bộ luật với RunWorkflow ───────────────────────
    private static Task<Result<AiWorkflowAvailabilityResponse>> Availability(AiHarness h, Guid user, Guid? scene = null, bool withScene = true) =>
        new GetWorkflowAvailabilityQueryHandler(h.Db, h.Auth).Handle(
            new GetWorkflowAvailabilityQuery(AiDemoSeeder.SeriesId, user, AiDemoSeeder.ChapterId,
                withScene ? scene ?? AiDemoSeeder.SceneId : null), default);

    [Fact]
    public async Task Availability_ListsAllSevenWorkflows_WithAgentsRolesAndMissingConditions()
    {
        var h = await AiHarness.CreateAsync();
        var res = (await Availability(h, AiHarness.Writer)).Value!;

        res.YourRole.Should().Be(WorkspaceRole.Writer);
        res.Workflows.Select(w => w.WorkflowType).Should().BeEquivalentTo(Enum.GetValues<AiWorkflowType>());
        res.Workflows.Single(w => w.WorkflowType == AiWorkflowType.Script).CanRun.Should().BeTrue();
        res.Workflows.Single(w => w.WorkflowType == AiWorkflowType.FullScene).Agents.Should().Equal("Writer", "Continuity", "Storyboard", "Editor");

        var artist = res.Workflows.Single(w => w.WorkflowType == AiWorkflowType.Artist);
        artist.CanRun.Should().BeFalse();
        artist.Missing.Should().Contain(m => m.Code == "ROLE_NOT_ALLOWED" && m.Kind == "Permission");
        artist.Missing.Should().Contain(m => m.Code == "NO_LAYOUT" && m.Kind == "Context");
        artist.RequiredRoles.Should().Equal(WorkspaceRole.Artist);
        artist.ResultKinds.Should().Equal("ImagePrompt");

        var storyboard = res.Workflows.Single(w => w.WorkflowType == AiWorkflowType.Storyboard);
        storyboard.Missing.Should().Contain(m => m.Code == "NO_SCRIPT");
        res.Workflows.Single(w => w.WorkflowType == AiWorkflowType.Editor).ResultKinds.Should().Equal("Report");
        res.Workflows.Single(w => w.WorkflowType == AiWorkflowType.Producer).ResultKinds.Should().Equal("Report");
    }

    [Fact]
    public async Task Availability_NonMember_Forbidden_UnknownChapter_NotFound_NoScene_SaysSceneRequired()
    {
        var h = await AiHarness.CreateAsync();

        (await Availability(h, AiHarness.Reader)).ErrorCode.Should().Be(ResultErrorCodes.Forbidden);
        (await new GetWorkflowAvailabilityQueryHandler(h.Db, h.Auth).Handle(
            new GetWorkflowAvailabilityQuery(AiDemoSeeder.SeriesId, AiHarness.Writer, Guid.NewGuid(), null), default))
            .ErrorCode.Should().Be(ResultErrorCodes.NotFound);

        var noScene = (await Availability(h, AiHarness.Writer, withScene: false)).Value!;
        noScene.Workflows.Single(w => w.WorkflowType == AiWorkflowType.Script).Missing.Should().Contain(m => m.Code == "SCENE_REQUIRED");
        (await Availability(h, AiHarness.Producer, withScene: false)).Value!
            .Workflows.Single(w => w.WorkflowType == AiWorkflowType.Producer).CanRun.Should().BeTrue();
    }

    [Fact]
    public async Task Availability_ShowsLockedChapter_AiDisabled_AndAssignmentBlocks()
    {
        var h = await AiHarness.CreateAsync();
        await AssignAsync(h, AiHarness.Editor, WorkspaceRole.Writer);
        var assigned = (await Availability(h, AiHarness.Writer)).Value!.Workflows.Single(w => w.WorkflowType == AiWorkflowType.Script);
        assigned.Missing.Should().Contain(m => m.Code == "ASSIGNMENT_REQUIRED");

        await LockChapterAsync(h);
        (await Availability(h, AiHarness.Producer)).Value!.Workflows.Single(w => w.WorkflowType == AiWorkflowType.Script)
            .Missing.Should().Contain(m => m.Code == "CHAPTER_LOCKED");
        (await Availability(h, AiHarness.Producer)).Value!.Workflows.Single(w => w.WorkflowType == AiWorkflowType.Producer).CanRun.Should().BeTrue();

        (await h.Db.WorkspaceAiConfigs.SingleAsync()).UpdateSettings("Google Gemini", "ciphertext", false, 1_000_000, "[]");
        await h.Db.SaveChangesAsync();
        (await Availability(h, AiHarness.Producer)).Value!.Workflows.Should().OnlyContain(w => !w.CanRun && w.Missing.Any(m => m.Code == "AI_DISABLED"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Availability_CanRun_MatchesWhatRunWorkflowActuallyDoes_ForEveryRoleAndWorkflow(bool afterLayoutAccepted)
    {
        var h = await AiHarness.CreateAsync();
        if (afterLayoutAccepted) await h.AcceptScriptAndLayoutAsync();

        foreach (var user in Members)
        {
            var availability = (await Availability(h, UserOf(user))).Value!;
            foreach (var w in availability.Workflows)
            {
                var run = await h.RunHandler().Handle(await h.CommandAsync(w.WorkflowType, UserOf(user)), default);
                run.IsSuccess.Should().Be(w.CanRun, $"{user} / {w.WorkflowType}: {string.Join(";", w.Missing.Select(m => m.Code))} vs {run.ErrorMessage}");
            }
        }
    }

    // ── helpers ──────────────────────────────────────────────────────────────
    private static async Task LockChapterAsync(AiHarness h)
    {
        var chapter = await h.Db.Chapters.SingleAsync();
        typeof(Chapter).GetProperty(nameof(Chapter.Status))!.SetValue(chapter, ChapterStatus.Locked);
        await h.Db.SaveChangesAsync();
    }

    private static async Task AssignAsync(
        AiHarness h, Guid assignee, WorkspaceRole role, AssignmentStatus status = AssignmentStatus.Assigned,
        DateTime? due = null, Guid? stageId = null)
    {
        var a = Assignment.Create(AiDemoSeeder.SeriesId, AiDemoSeeder.ChapterId, WorkflowEntityType.Page, Guid.NewGuid(),
            stageId ?? Guid.NewGuid(), assignee, role, "Brief giao việc", dueDate: due);
        switch (status)
        {
            case AssignmentStatus.InProgress: a.StartWork(); break;
            case AssignmentStatus.Submitted: a.SubmitForReview(); break;
            case AssignmentStatus.Approved: a.MarkApproved(); break;
            case AssignmentStatus.ReworkRequired: a.RequestRework("Sửa lại"); break;
        }
        h.Db.Assignments.Add(a);
        await h.Db.SaveChangesAsync();
    }
}
