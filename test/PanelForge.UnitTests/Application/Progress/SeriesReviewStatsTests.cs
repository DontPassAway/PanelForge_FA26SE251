using FluentAssertions;
using PanelForge.Application.Features.Progress.Queries.GetSeriesReviewStats;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Entities.Workspaces;
using PanelForge.Domain.Enums;
using PanelForge.Infrastructure.Persistence;
using PanelForge.UnitTests.Common;
using Xunit;
using DomainSeries = PanelForge.Domain.Entities.Content.Series;

namespace PanelForge.UnitTests.Application.Progress;

/// <summary>CF3: tổng quan duyệt của Series cho Editor.</summary>
public class SeriesReviewStatsTests
{
    private readonly PanelForgeDbContext _db = InMemoryDb.Create();
    private readonly DomainSeries _series;
    private readonly Chapter _chapter;
    private readonly Guid _artist = Guid.NewGuid();
    private readonly Guid _editor = Guid.NewGuid();

    public SeriesReviewStatsTests()
    {
        var studio = StudioWorkspace.Create("Studio", Guid.NewGuid());
        _series = DomainSeries.Create(studio.Id, "Moon", ReadingDirection.RightToLeft);
        _chapter = Chapter.Create(_series.Id, 1);
        _db.StudioWorkspaces.Add(studio);
        _db.Series.Add(_series);
        _db.Chapters.Add(_chapter);
        _db.SaveChanges();
    }

    private Assignment NewAssignment() => Assignment.Create(
        _series.Id, _chapter.Id, WorkflowEntityType.Page, Guid.NewGuid(), Guid.NewGuid(), _artist, WorkspaceRole.Artist, "Vẽ");

    [Fact]
    public async Task ReviewStats_CountsQueueChangeRequestsFindingsAndDecisions()
    {
        var waiting = NewAssignment(); waiting.StartWork(); waiting.SubmitForReview();
        var reviewing = NewAssignment(); reviewing.StartWork(); reviewing.SubmitForReview(); reviewing.OpenReview();
        _db.Assignments.AddRange(waiting, reviewing);

        var open = ChangeRequest.Create(waiting.Id, _chapter.Id, null, _artist, _editor, "Sửa mắt", dueDate: DateTime.UtcNow.AddDays(-1));
        var resolved = ChangeRequest.Create(waiting.Id, _chapter.Id, null, _artist, _editor, "Sửa tóc");
        resolved.Resolve("đã sửa");
        _db.ChangeRequests.AddRange(open, resolved);

        _db.ConsistencyFindings.AddRange(
            ConsistencyFinding.Create(_chapter.Id, "SPEAKER_MISSING", ConsistencyFindingSeverity.Blocking, "Thiếu người nói", "fp-1"),
            ConsistencyFinding.Create(_chapter.Id, "RULE_PATTERN", ConsistencyFindingSeverity.Warning, "Sai tên", "fp-2"));

        var firstPass = Submission.Create(Guid.NewGuid(), _chapter.Id, _artist, 1);
        firstPass.Approve(_editor);
        var rejected = Submission.Create(Guid.NewGuid(), _chapter.Id, _artist, 1);
        rejected.Reject(_editor, "Sai bố cục");
        var selfApproved = Submission.Create(Guid.NewGuid(), _chapter.Id, _artist, 1);
        selfApproved.Approve(_artist);
        _db.Submissions.AddRange(firstPass, rejected, selfApproved);
        await _db.SaveChangesAsync();

        var result = await new GetSeriesReviewStatsQueryHandler(_db).Handle(new GetSeriesReviewStatsQuery(_series.Id), default);

        result.IsSuccess.Should().BeTrue();
        var c = result.Value!.Chapters.Should().ContainSingle().Subject;
        (c.WaitingReview, c.InReview).Should().Be((1, 1));
        (c.OpenChangeRequests, c.ResolvedChangeRequests, c.OverdueChangeRequests).Should().Be((1, 1, 1));
        (c.OpenFindings, c.BlockingFindings).Should().Be((2, 1));
        (c.Approved, c.Rejected, c.FirstPassApproved).Should().Be((1, 1, 1), "lần tự duyệt không tính");
        result.Value.Decisions.Should().HaveCount(GetSeriesReviewStatsQueryHandler.WindowDays);
        result.Value.Decisions.Last().Should().Be(new ReviewDecisionPointDto(DateOnly.FromDateTime(DateTime.UtcNow), 1, 1));
        result.Value.AvgReviewHours.Should().NotBeNull();
    }

    [Fact]
    public async Task ReviewStats_UnknownSeries_ReturnsNotFound()
    {
        var result = await new GetSeriesReviewStatsQueryHandler(_db).Handle(new GetSeriesReviewStatsQuery(Guid.NewGuid()), default);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(PanelForge.Application.Common.ResultErrorCodes.NotFound);
    }
}
