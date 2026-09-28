using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Domain.Entities.MasterData;

namespace PanelForge.Application.Features.MasterData.PipelineTemplates.Commands.DeactivatePipelineTemplate;

public sealed record DeactivatePipelineTemplateCommand(Guid Id) : IRequest<Result<string>>;

public sealed class DeactivatePipelineTemplateCommandHandler
    : IRequestHandler<DeactivatePipelineTemplateCommand, Result<string>>
{
    private readonly IPanelForgeDbContext _dbContext;
    public DeactivatePipelineTemplateCommandHandler(IPanelForgeDbContext dbContext) => _dbContext = dbContext;

    public async Task<Result<string>> Handle(DeactivatePipelineTemplateCommand cmd, CancellationToken ct)
    {
        var template = await _dbContext.PipelineTemplates.FirstOrDefaultAsync(t => t.Id == cmd.Id, ct);
        if (template is null) return Result<string>.Failure("PipelineTemplate không tồn tại.");
        template.Deactivate();
        await _dbContext.SaveChangesAsync(ct);
        return Result<string>.Success($"PipelineTemplate '{template.Code}' đã được vô hiệu hóa.");
    }
}
