using System.Text.Json;
using FluentAssertions;
using Moq;
using Microsoft.EntityFrameworkCore;
using PanelForge.AI.Contracts;
using PanelForge.Application.Common;
using PanelForge.Application.Features.AiAgents.Queries.CompareRevisions;
using PanelForge.Application.Features.AiAgents.Queries.GetProvenanceReport;
using PanelForge.Application.Features.AiAgents.Queries.GetRunHistory;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;
using PanelForge.Infrastructure.Persistence.Seeders;
using Xunit;

namespace PanelForge.UnitTests.Ai;

/// <summary>CF4: lịch sử AI run, so sánh revision (UC-18), Provenance Report (UC-19, BR-10), quyền xem (BR-07).</summary>
public class HistoryProvenanceTests
{
    private static Task<Result<ProvenanceReportResponse>> Report(AiHarness h, Guid user, Guid? sceneId = null) =>
        new GetProvenanceReportQueryHandler(h.Db, h.Auth, h.Pages.Object).Handle(
            new GetProvenanceReportQuery(AiDemoSeeder.SeriesId, AiDemoSeeder.ChapterId, sceneId, user), default);

    private static Task<Result<RevisionDiffResponse>> Compare(AiHarness h, Guid left, Guid right, Guid? user = null) =>
        new CompareRevisionsQueryHandler(h.Db, h.Auth).Handle(
            new CompareRevisionsQuery(AiDemoSeeder.SeriesId, AiDemoSeeder.SceneId, left, right, user ?? AiHarness.Producer), default);

    private static async Task<ContentRevision> AddRevisionAsync(
        AiHarness h, ContentRevisionKind kind, int version, string payload, Guid? previous = null)
    {
        var revision = ContentRevision.Create(AiDemoSeeder.ChapterId, AiDemoSeeder.SceneId, kind, version,
            $"v{version}", payload, $"hash-{kind}-{version}", previous, null);
        h.Db.ContentRevisions.Add(revision);
        await h.Db.SaveChangesAsync();
        return revision;
    }

    [Fact]
    public async Task Report_TracesRunToApprovedRevision_WithModelPromptAndDecider()
    {
        var h = await AiHarness.CreateAsync();
        var runId = await h.CompleteDemoRunAsync();
        var script = await h.SuggestionAsync(runId, AiSuggestionKind.Script);
        (await h.ReviewAsync(script.Id, AiSuggestionStatus.Accepted, reason: "Đúng ý")).IsSuccess.Should().BeTrue();

        var result = await Report(h, AiHarness.Producer);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        var report = result.Value!;
        var revision = await h.Db.ContentRevisions.SingleAsync();
        var decision = await h.Db.AiSuggestionDecisions.SingleAsync();

        // Chuỗi Run → Step → Suggestion → Decision → Revision nối liền (BR-10)
        report.Edges.Should().Contain(e => e.FromId == runId && e.Relationship == "produced");
        report.Edges.Should().Contain(e => e.FromId == script.StepId && e.ToId == script.Id && e.Relationship == "produced");
        report.Edges.Should().Contain(e => e.FromId == script.Id && e.ToId == decision.Id && e.Relationship == "decided");
        report.Edges.Should().Contain(e => e.FromId == decision.Id && e.ToId == revision.Id && e.Relationship == "created");
        report.Edges.Should().Contain(e => e.FromId == script.Id && e.ToId == revision.Id && e.Relationship == "originatedFrom");

        // Step mang model + phiên bản prompt; Decision mang người quyết + mức sửa
        var step = JsonSerializer.SerializeToElement(report.Nodes.First(n => n.NodeType == "Step").Details);
        step.GetProperty("PromptRef").GetString().Should().NotBeNullOrWhiteSpace();
        step.GetProperty("Model").GetString().Should().NotBeNullOrWhiteSpace();
        var dec = JsonSerializer.SerializeToElement(report.Nodes.Single(n => n.NodeType == "Decision").Details);
        dec.GetProperty("decidedByUserId").GetGuid().Should().Be(AiHarness.Writer);
        dec.GetProperty("ModificationLevel").GetString().Should().NotBeNull();

        report.Stats.AcceptedSuggestions.Should().Be(1);
        report.Stats.AiOriginatedRevisions.Should().Be(1);
        report.Timeline.Select(t => t.Timestamp).Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task Report_ForEditor_IsAllowed_ButWriterAndArtistAreForbidden()
    {
        var h = await AiHarness.CreateAsync();

        (await Report(h, AiHarness.Editor)).IsSuccess.Should().BeTrue();
        (await Report(h, AiHarness.Writer)).ErrorCode.Should().Be(ResultErrorCodes.Forbidden);
        (await Report(h, AiHarness.Artist)).ErrorCode.Should().Be(ResultErrorCodes.Forbidden);
    }

    [Fact]
    public async Task Report_WithSceneOutsideChapter_IsNotFound()
    {
        var h = await AiHarness.CreateAsync();
        (await Report(h, AiHarness.Producer, Guid.NewGuid())).ErrorCode.Should().Be(ResultErrorCodes.NotFound);
    }

    [Fact]
    public async Task Compare_AiObjectPayload_WithManualArrayPayload_DiffsByLineId()
    {
        var h = await AiHarness.CreateAsync();
        Guid a = Guid.NewGuid(), b = Guid.NewGuid(), c = Guid.NewGuid();
        // v1 dạng đề xuất AI: object có "lines"; v2 dạng sửa tay: mảng dòng
        var v1 = await AddRevisionAsync(h, ContentRevisionKind.Script, 1, AiJson.Serialize(new
        {
            lines = new[]
            {
                new BaselineScriptLine(a, 1, null, "Xin chào", null),
                new BaselineScriptLine(b, 2, null, "Đi thôi", null)
            }
        }));
        var v2 = await AddRevisionAsync(h, ContentRevisionKind.Script, 2, AiJson.Serialize(new[]
        {
            new BaselineScriptLine(a, 1, null, "Chào cậu", null),
            new BaselineScriptLine(c, 2, null, "Dòng mới", null),
            new BaselineScriptLine(b, 3, null, "Đi thôi", null)
        }), v1.Id);

        var result = await Compare(h, v1.Id, v2.Id);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value!.Differences.Select(d => (d.Path, d.ChangeType)).Should().BeEquivalentTo(new[]
        {
            ($"line:{a}", "Modified"),
            ($"line:{c}", "Added"),
            ($"line:{b}", "Moved")
        });
        result.Value.LeftPayload.Should().NotBeNull();
    }

    [Fact]
    public async Task Compare_DifferentKinds_IsValidationError()
    {
        var h = await AiHarness.CreateAsync();
        var script = await AddRevisionAsync(h, ContentRevisionKind.Script, 1, "[]");
        var layout = await AddRevisionAsync(h, ContentRevisionKind.Layout, 1, "{}");

        (await Compare(h, script.Id, layout.Id)).ErrorCode.Should().Be(ResultErrorCodes.Validation);
    }

    [Fact]
    public async Task Compare_ByNonMember_IsForbidden()
    {
        var h = await AiHarness.CreateAsync();
        var v1 = await AddRevisionAsync(h, ContentRevisionKind.Script, 1, "[]");

        (await Compare(h, v1.Id, v1.Id, Guid.NewGuid())).ErrorCode.Should().Be(ResultErrorCodes.Forbidden);
    }

    [Fact]
    public async Task RunHistory_FiltersByChapter_AndCountsDecisions()
    {
        var h = await AiHarness.CreateAsync();
        var runId = await h.CompleteDemoRunAsync();
        var script = await h.SuggestionAsync(runId, AiSuggestionKind.Script);
        await h.ReviewAsync(script.Id, AiSuggestionStatus.Rejected, reason: "Chưa đạt");

        var history = await new GetAiRunHistoryQueryHandler(h.Db, h.Auth).Handle(
            new GetAiRunHistoryQuery(AiDemoSeeder.SeriesId, AiHarness.Producer, ChapterId: AiDemoSeeder.ChapterId), default);
        var otherChapter = await new GetAiRunHistoryQueryHandler(h.Db, h.Auth).Handle(
            new GetAiRunHistoryQuery(AiDemoSeeder.SeriesId, AiHarness.Producer, ChapterId: Guid.NewGuid()), default);

        history.Value!.Items.Should().ContainSingle(r => r.Id == runId && r.RejectedCount == 1 && r.StepCount > 0);
        otherChapter.Value!.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task Report_IncludesArtistArtwork_LettererHistory_AndEditorReviews_FromExistingData()
    {
        var h = await AiHarness.CreateAsync();
        var chapterId = AiDemoSeeder.ChapterId;
        var page = Page.CreateNew(chapterId, 99, LayoutFormat.StandardPage, 1200, 1800, 300);
        h.Db.Pages.Add(page);
        var def = PanelForge.Domain.Entities.Workflow.PipelineDefinition.Create(Guid.NewGuid(), "P");
        var ink = PanelForge.Domain.Entities.Workflow.PipelineStage.Create(def.Id, "Ink", "ink", 1, allowedRole: WorkspaceRole.Artist, isInitial: true);
        h.Db.PipelineStages.Add(ink);
        // Artist: 2 phiên bản artwork của cùng trang/stage
        var art1 = PanelForge.Domain.Entities.Assets.AssetReference.Create(Guid.NewGuid(), Guid.NewGuid(), chapterId, page.Id, ink.Id, 1, null, "ink-v1.png", AiHarness.Artist);
        var art2 = PanelForge.Domain.Entities.Assets.AssetReference.Create(Guid.NewGuid(), Guid.NewGuid(), chapterId, page.Id, ink.Id, 2, null, "ink-v2.png", AiHarness.Artist);
        h.Db.AssetReferences.AddRange(art1, art2);
        // Editor: lần 1 trả về, lần 2 duyệt
        var job = PanelForge.Domain.Entities.Workflow.Assignment.Create(AiDemoSeeder.SeriesId, chapterId, WorkflowEntityType.Page,
            page.Id, ink.Id, AiHarness.Artist, WorkspaceRole.Artist, "Vẽ trang 99");
        h.Db.Assignments.Add(job);
        var s1 = PanelForge.Domain.Entities.Workflow.Submission.Create(job.Id, chapterId, AiHarness.Artist, 1, [art1.Id]);
        s1.Reject(AiHarness.Editor, "Thiếu nền");
        var s2 = PanelForge.Domain.Entities.Workflow.Submission.Create(job.Id, chapterId, AiHarness.Artist, 2, [art2.Id]);
        s2.Approve(AiHarness.Editor);
        h.Db.Submissions.AddRange(s1, s2);
        await h.Db.SaveChangesAsync();

        // Letterer: đặt bóng, 3 lần kéo liên tiếp (gộp làm 1), đổi kiểu; layer ảnh không tính
        page.AddElement("SPEECH_BALLOON", 10, 10, 300, 200, 1, AiHarness.Letterer, "Chạy đi!");
        page.AddElement("ARTWORK_LAYER", 0, 0, 1200, 1800, 0, AiHarness.Artist);
        var balloon = page.Elements.Values.Single(e => e.ElementType == "SPEECH_BALLOON").ElementId;
        var art = page.Elements.Values.Single(e => e.ElementType == "ARTWORK_LAYER").ElementId;
        page.MoveElement(balloon, 20, 10, 300, 200, AiHarness.Letterer);
        page.MoveElement(balloon, 30, 10, 300, 200, AiHarness.Letterer);
        page.MoveElement(balloon, 40, 10, 300, 200, AiHarness.Letterer);
        page.MoveElement(art, 5, 5, 1200, 1800, AiHarness.Artist);
        page.UpdateElementStyle(balloon, new TextStyle("Mali", 30, BalloonShape: "none"), AiHarness.Letterer);
        h.Pages.Setup(p => p.GetEventsAsync(page.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(page.UncommittedEvents.ToList());

        var report = (await Report(h, AiHarness.Editor)).Value!;

        string Kind(ProvenanceNode n) => JsonSerializer.SerializeToElement(n.Details).GetProperty("kind").ToString();
        var revisions = report.Nodes.Where(n => n.NodeType == "Revision").ToList();

        var artwork = revisions.Where(n => Kind(n) == "Artwork").ToList();
        artwork.Select(n => n.Id).Should().Contain([art1.Id, art2.Id]);
        report.Edges.Should().Contain(new ProvenanceEdge(art1.Id, art2.Id, "previousVersion"));

        var lettering = revisions.Where(n => Kind(n) == "Lettering").ToList();
        lettering.Should().HaveCount(3); // đặt bóng, kéo ×3 (gộp), đổi kiểu — không có layer ảnh
        lettering.Should().Contain(n => n.Summary.Contains("(3 lần)"));
        lettering.Should().Contain(n => n.Summary.Contains("bóng none"));

        var reviews = revisions.Where(n => Kind(n) == "Review").ToList();
        reviews.Should().Contain(n => n.Id == s1.Id && n.Summary.Contains("trả về") && n.Summary.Contains("Thiếu nền"));
        reviews.Should().Contain(n => n.Id == s2.Id && n.Summary.Contains("đã duyệt"));
        report.Edges.Should().Contain(new ProvenanceEdge(s1.Id, s2.Id, "previousVersion"));
        report.Timeline.Should().Contain(t => t.EventType == "ReviewDecided" && t.EntityId == s1.Id);

        // Lọc theo Scene: chỉ còn nội dung kịch bản (lịch sử theo trang không gắn Scene)
        var sceneId = await h.Db.Scenes.Where(s => s.ChapterId == chapterId).Select(s => s.Id).FirstAsync();
        var scoped = (await Report(h, AiHarness.Editor, sceneId)).Value!;
        scoped.Nodes.Should().NotContain(n => n.Id == art1.Id || n.Id == s1.Id);
    }
}
