using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.MasterData.PipelineTemplates.Commands;
using PanelForge.Application.Interfaces.Persistence;

namespace PanelForge.Application.Features.MasterData.PipelineTemplates.Queries;

public sealed record GetPipelineTemplatesQuery() : IRequest<Result<IReadOnlyList<PipelineTemplateDto>>>;

public sealed class GetPipelineTemplatesQueryHandler
    : IRequestHandler<GetPipelineTemplatesQuery, Result<IReadOnlyList<PipelineTemplateDto>>>
{
    private readonly IPanelForgeDbContext _dbContext;
    public GetPipelineTemplatesQueryHandler(IPanelForgeDbContext dbContext) => _dbContext = dbContext;

    public async Task<Result<IReadOnlyList<PipelineTemplateDto>>> Handle(GetPipelineTemplatesQuery q, CancellationToken ct)
    {
        var list = await _dbContext.PipelineTemplates
            .Include(t => t.Stages)
            .OrderByDescending(t => t.IsDefault).ThenBy(t => t.Code)
            .ToListAsync(ct);
        return Result<IReadOnlyList<PipelineTemplateDto>>.Success(list.Select(t => t.ToDto()).ToList());
    }
}

public sealed record GetPipelineTemplateByIdQuery(Guid Id) : IRequest<Result<PipelineTemplateDto>>;

public sealed class GetPipelineTemplateByIdQueryHandler
    : IRequestHandler<GetPipelineTemplateByIdQuery, Result<PipelineTemplateDto>>
{
    private readonly IPanelForgeDbContext _dbContext;
    public GetPipelineTemplateByIdQueryHandler(IPanelForgeDbContext dbContext) => _dbContext = dbContext;

    public async Task<Result<PipelineTemplateDto>> Handle(GetPipelineTemplateByIdQuery q, CancellationToken ct)
    {
        var t = await _dbContext.PipelineTemplates
            .Include(x => x.Stages)
            .FirstOrDefaultAsync(x => x.Id == q.Id, ct);

        if (t is null) return Result<PipelineTemplateDto>.Failure("PipelineTemplate không tồn tại.");
        return Result<PipelineTemplateDto>.Success(t.ToDto());
    }
}
