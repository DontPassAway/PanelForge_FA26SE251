using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.MasterData.PipelineTemplates.Models;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.MasterData.PipelineTemplates.Common;

namespace PanelForge.Application.Features.MasterData.PipelineTemplates.Queries.GetPipelineTemplateById;

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
