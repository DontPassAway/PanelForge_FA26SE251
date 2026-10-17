using System.Text.Json;
using FluentAssertions;
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
        new GetProvenanceReportQueryHandler(h.Db, h.Auth).Handle(
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
}
