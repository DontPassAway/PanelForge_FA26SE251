using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.SeriesPipeline.Models;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.SeriesPipeline.Common;

namespace PanelForge.Application.Features.SeriesPipeline.Queries.GetSeriesPipeline;

public sealed record GetSeriesPipelineQuery(Guid SeriesId) : IRequest<Result<SeriesPipelineDto>>;

public sealed class GetSeriesPipelineQueryHandler : IRequestHandler<GetSeriesPipelineQuery, Result<SeriesPipelineDto>>
{
    private readonly IPanelForgeDbContext _dbContext;
    public GetSeriesPipelineQueryHandler(IPanelForgeDbContext dbContext) => _dbContext = dbContext;

    public async Task<Result<SeriesPipelineDto>> Handle(GetSeriesPipelineQuery query, CancellationToken ct)
    {
        var pipeline = await SeriesPipelineStore.LoadAsync(_dbContext, query.SeriesId, ct);
        return pipeline is null
            ? SeriesPipelineStore.NotFound(query.SeriesId)
            : Result<SeriesPipelineDto>.Success(pipeline.ToDto(query.SeriesId));
    }
}
