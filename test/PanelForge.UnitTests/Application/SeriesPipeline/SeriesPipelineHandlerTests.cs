using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.SeriesPipeline;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Enums;
using PanelForge.Infrastructure.Persistence;
using PanelForge.UnitTests.Common;
using Xunit;
using DomainSeries = PanelForge.Domain.Entities.Content.Series;

namespace PanelForge.UnitTests.Application.SeriesPipeline;

/// <summary>UC-03 bước 3: API Producer cấu hình pipeline của Series (chạy trên DbContext InMemory thật).</summary>
public class SeriesPipelineHandlerTests
{
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly Guid _seriesId;
    private readonly List<Guid> _stageIds;

    public SeriesPipelineHandlerTests()
    {
        using var db = NewDb();
        var workspaceId = Guid.NewGuid();
        var series = DomainSeries.Create(workspaceId, "Series", ReadingDirection.RightToLeft);
        var pipeline = PipelineDefinition.Create(workspaceId, "Pipeline", seriesId: series.Id);
        pipeline.InsertStage("Script", null, null, null, WorkspaceRole.Writer, false, null);
        pipeline.InsertStage("Pencil", null, null, null, WorkspaceRole.Artist, false, null);
        pipeline.InsertStage("Review", null, null, null, WorkspaceRole.Editor, true, null);
        pipeline.InsertStage("Approved", null, null, null, WorkspaceRole.Editor, false, null);
        pipeline.RebuildDefaultTransitions();
        series.SetPipelineDefinition(pipeline.Id);

        db.Series.Add(series);
        db.PipelineDefinitions.Add(pipeline);
        db.SaveChanges();

        _seriesId = series.Id;
        _stageIds = pipeline.GetOrderedStages().Select(s => s.Id).ToList();
    }

    private PanelForgeDbContext NewDb() => InMemoryDb.Create(databaseName: _dbName);

    [Fact]
    public async Task Get_ShouldReturnOrderedStagesAndTransitions()
    {
        await using var db = NewDb();

        var result = await new GetSeriesPipelineQueryHandler(db).Handle(new GetSeriesPipelineQuery(_seriesId), default);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Stages.Select(s => s.Name).Should().Equal("Script", "Pencil", "Review", "Approved");
        result.Value.Transitions.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Get_UnknownSeries_ShouldReturnNotFound()
    {
        await using var db = NewDb();

        var result = await new GetSeriesPipelineQueryHandler(db).Handle(new GetSeriesPipelineQuery(Guid.NewGuid()), default);

        result.ErrorCode.Should().Be(ResultErrorCodes.NotFound);
    }

    [Fact]
    public async Task AddStage_ShouldPersistStage_AndRebuildTransitions()
    {
        await using (var db = NewDb())
        {
            var result = await new AddPipelineStageCommandHandler(db).Handle(new AddPipelineStageCommand(
                _seriesId, "Ink", null, 3, "#10B981", WorkspaceRole.Artist, false, 2), default);
            result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        }

        await using var verify = NewDb();
        var stages = await verify.PipelineStages.OrderBy(s => s.StageOrder).ToListAsync();
        stages.Select(s => s.Name).Should().Equal("Script", "Pencil", "Ink", "Review", "Approved");

        var ink = stages.Single(s => s.Name == "Ink");
        var pencil = stages.Single(s => s.Name == "Pencil");
        var active = await verify.StageTransitions.ToListAsync();
        active.Should().Contain(t => t.FromStageId == pencil.Id && t.ToStageId == ink.Id);
        active.Should().NotContain(t => t.FromStageId == pencil.Id && t.ToStageId == stages.Single(s => s.Name == "Review").Id,
            "transition cũ Pencil → Review phải bị gỡ khi chèn Ink vào giữa");
    }

    [Fact]
    public async Task Reorder_ShouldPersistNewOrder()
    {
        await using (var db = NewDb())
        {
            var result = await new ReorderPipelineStagesCommandHandler(db).Handle(new ReorderPipelineStagesCommand(
                _seriesId, [_stageIds[1], _stageIds[0], _stageIds[2], _stageIds[3]]), default);
            result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        }

        await using var verify = NewDb();
        var first = await verify.PipelineStages.SingleAsync(s => s.StageOrder == 1);
        first.Id.Should().Be(_stageIds[1]);
        first.IsInitial.Should().BeTrue();
    }

    [Fact]
    public async Task Update_ShouldChangeRole_AndRequiredRoleOfOutgoingTransition()
    {
        await using (var db = NewDb())
        {
            var result = await new UpdatePipelineStageCommandHandler(db).Handle(new UpdatePipelineStageCommand(
                _seriesId, _stageIds[0], "Kịch bản", "#6B7280", WorkspaceRole.Producer, false, 3), default);
            result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        }

        await using var verify = NewDb();
        (await verify.PipelineStages.SingleAsync(s => s.Id == _stageIds[0])).Name.Should().Be("Kịch bản");
        var outgoing = await verify.StageTransitions.SingleAsync(t => t.FromStageId == _stageIds[0]);
        outgoing.RequiredRole.Should().Be(WorkspaceRole.Producer);
    }

    [Fact]
    public async Task Update_StageOfAnotherPipeline_ShouldReturnNotFound()
    {
        await using var db = NewDb();

        var result = await new UpdatePipelineStageCommandHandler(db).Handle(new UpdatePipelineStageCommand(
            _seriesId, Guid.NewGuid(), "X", null, null, false, null), default);

        result.ErrorCode.Should().Be(ResultErrorCodes.NotFound);
    }

    [Fact]
    public async Task Delete_UnusedStage_ShouldSoftDelete_AndAllowReusingSlug()
    {
        await using (var db = NewDb())
        {
            var result = await new DeletePipelineStageCommandHandler(db).Handle(
                new DeletePipelineStageCommand(_seriesId, _stageIds[1]), default);
            result.IsSuccess.Should().BeTrue(result.ErrorMessage);
            result.Value!.Stages.Select(s => s.Name).Should().Equal("Script", "Review", "Approved");
        }

        await using (var db = NewDb())
        {
            // AsNoTracking: không để stage đã xóa bị fixup vào pipeline.Stages của context này
            (await db.PipelineStages.IgnoreQueryFilters().AsNoTracking().SingleAsync(s => s.Id == _stageIds[1]))
                .IsDeleted.Should().BeTrue();

            // Slug "pencil" của stage đã xóa không giữ chỗ
            var readd = await new AddPipelineStageCommandHandler(db).Handle(new AddPipelineStageCommand(
                _seriesId, "Pencil", "pencil", 2, null, WorkspaceRole.Artist, false, null), default);
            readd.IsSuccess.Should().BeTrue(readd.ErrorMessage);
        }
    }

    [Fact]
    public async Task Delete_StageWithActiveAssignment_ShouldReturnConflict()
    {
        await using (var db = NewDb())
        {
            db.Assignments.Add(Assignment.Create(
                _seriesId, Guid.NewGuid(), WorkflowEntityType.Page, Guid.NewGuid(),
                _stageIds[1], Guid.NewGuid(), WorkspaceRole.Artist, "Pencil page 3"));
            await db.SaveChangesAsync();
        }

        await using var verify = NewDb();
        var result = await new DeletePipelineStageCommandHandler(verify).Handle(
            new DeletePipelineStageCommand(_seriesId, _stageIds[1]), default);

        result.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        (await verify.PipelineStages.CountAsync()).Should().Be(4);
    }
}
