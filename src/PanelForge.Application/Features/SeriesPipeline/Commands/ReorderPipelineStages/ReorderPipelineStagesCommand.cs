using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.SeriesPipeline.Models;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.SeriesPipeline.Common;

namespace PanelForge.Application.Features.SeriesPipeline.Commands.ReorderPipelineStages;

public sealed record ReorderPipelineStagesCommand(Guid SeriesId, IReadOnlyList<Guid> StageIds)
    : IRequest<Result<SeriesPipelineDto>>;

public sealed class ReorderPipelineStagesCommandHandler : IRequestHandler<ReorderPipelineStagesCommand, Result<SeriesPipelineDto>>
{
    private readonly IPanelForgeDbContext _dbContext;
    public ReorderPipelineStagesCommandHandler(IPanelForgeDbContext dbContext) => _dbContext = dbContext;

    public async Task<Result<SeriesPipelineDto>> Handle(ReorderPipelineStagesCommand cmd, CancellationToken ct)
    {
        var pipeline = await SeriesPipelineStore.LoadAsync(_dbContext, cmd.SeriesId, ct);
        if (pipeline is null) return SeriesPipelineStore.NotFound(cmd.SeriesId);

        try
        {
            pipeline.ReorderStages(cmd.StageIds ?? []);
        }
        catch (ArgumentException ex)
        {
            return Result<SeriesPipelineDto>.Failure(ex.Message);
        }

        SeriesPipelineStore.RebuildTransitions(_dbContext, pipeline);
        await _dbContext.SaveChangesAsync(ct);

        return Result<SeriesPipelineDto>.Success(pipeline.ToDto(cmd.SeriesId));
    }
}
