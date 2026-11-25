using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Enums;
using PanelForge.Infrastructure.Persistence.Seeders;
using Xunit;

namespace PanelForge.UnitTests.Ai;

/// <summary>Phạm vi khi từ chối đề xuất (BR-07) và layout AI cho Webtoon cuộn dọc.</summary>
public sealed class AiReviewScopeAndVerticalTests
{
    [Theory]
    [InlineData(AiSuggestionStatus.Rejected)]
    [InlineData(AiSuggestionStatus.ChangesRequested)]
    public async Task WriterOutsideAssignment_CannotDecide_ButProducerCan(AiSuggestionStatus decision)
    {
        var h = await AiHarness.CreateAsync();
        var runId = await h.RunMockAsync(AiWorkflowType.FullScene);
        var script = await h.SuggestionAsync(runId, AiSuggestionKind.Script);
        h.Db.Assignments.Add(Assignment.Create(AiDemoSeeder.SeriesId, AiDemoSeeder.ChapterId, WorkflowEntityType.Page, Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), WorkspaceRole.Writer, "Việc của người khác"));
        await h.Db.SaveChangesAsync();

        (await h.ReviewAsync(script.Id, decision, AiHarness.Writer, reason: "Chưa đạt")).ErrorCode.Should().Be(ResultErrorCodes.Forbidden);
        (await h.ReviewAsync(script.Id, decision, AiHarness.Producer, reason: "Chưa đạt")).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task VerticalLayout_CreatesWebtoonPages()
    {
        var h = await AiHarness.CreateAsync();
        var cmd = await h.CommandAsync(AiWorkflowType.FullScene) with { ReadingDirection = ReadingDirection.Vertical };
        var started = await h.RunHandler().Handle(cmd, default);
        started.IsSuccess.Should().BeTrue(started.ErrorMessage);
        await h.ExecuteWorkflowAsync(started.Value!.RunId);
        var before = await h.Db.Pages.Select(p => p.Id).ToListAsync();

        var script = await h.SuggestionAsync(started.Value.RunId, AiSuggestionKind.Script);
        var layout = await h.SuggestionAsync(started.Value.RunId, AiSuggestionKind.Layout);
        (await h.ReviewAsync(script.Id, AiSuggestionStatus.Accepted, AiHarness.Writer, 0)).IsSuccess.Should().BeTrue();
        var accepted = await h.ReviewAsync(layout.Id, AiSuggestionStatus.Accepted, AiHarness.Writer, 1);
        accepted.IsSuccess.Should().BeTrue(accepted.ErrorMessage);

        var created = await h.Db.Pages.Where(p => !before.Contains(p.Id)).ToListAsync();
        created.Should().NotBeEmpty().And.OnlyContain(p => p.LayoutFormat == LayoutFormat.WebtoonLongstrip && p.HeightPx > p.WidthPx);
    }
}
