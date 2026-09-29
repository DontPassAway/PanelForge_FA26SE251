using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.MasterData.PipelineTemplates.Models;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.MasterData.PipelineTemplates.Common;

namespace PanelForge.Application.Features.MasterData.PipelineTemplates.Queries.GetPipelineTemplates;

/// <param name="ActiveOnly">true: chỉ template đang hoạt động (dùng cho Producer khi tạo Series).</param>
public sealed record GetPipelineTemplatesQuery(bool ActiveOnly = false) : IRequest<Result<IReadOnlyList<PipelineTemplateDto>>>;

public sealed class GetPipelineTemplatesQueryHandler
    : IRequestHandler<GetPipelineTemplatesQuery, Result<IReadOnlyList<PipelineTemplateDto>>>
{
    private readonly IPanelForgeDbContext _dbContext;
    public GetPipelineTemplatesQueryHandler(IPanelForgeDbContext dbContext) => _dbContext = dbContext;

    public async Task<Result<IReadOnlyList<PipelineTemplateDto>>> Handle(GetPipelineTemplatesQuery q, CancellationToken ct)
    {
        var list = await _dbContext.PipelineTemplates
            .Include(t => t.Stages)
            .Where(t => !q.ActiveOnly || t.IsActive)
            .OrderByDescending(t => t.IsDefault).ThenBy(t => t.Code)
            .ToListAsync(ct);
        return Result<IReadOnlyList<PipelineTemplateDto>>.Success(list.Select(t => t.ToDto()).ToList());
    }
}
