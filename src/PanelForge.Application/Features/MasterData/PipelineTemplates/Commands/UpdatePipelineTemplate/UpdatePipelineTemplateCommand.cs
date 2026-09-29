using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Domain.Entities.MasterData;
using PanelForge.Domain.Enums;
using PanelForge.Application.Features.MasterData.PipelineTemplates.Models;
using PanelForge.Application.Features.MasterData.PipelineTemplates.Common;

namespace PanelForge.Application.Features.MasterData.PipelineTemplates.Commands.UpdatePipelineTemplate;

public sealed record UpdatePipelineTemplateCommand(
    Guid Id, string Name, string? Description, bool IsDefault, bool IsActive,
    IReadOnlyList<PipelineTemplateStageRequest>? Stages
) : IRequest<Result<PipelineTemplateDto>>;

public sealed class UpdatePipelineTemplateCommandHandler
    : IRequestHandler<UpdatePipelineTemplateCommand, Result<PipelineTemplateDto>>
{
    private readonly IPanelForgeDbContext _dbContext;
    public UpdatePipelineTemplateCommandHandler(IPanelForgeDbContext dbContext) => _dbContext = dbContext;

    public async Task<Result<PipelineTemplateDto>> Handle(UpdatePipelineTemplateCommand cmd, CancellationToken ct)
    {
        var template = await _dbContext.PipelineTemplates
            .Include(t => t.Stages)
            .FirstOrDefaultAsync(t => t.Id == cmd.Id, ct);

        if (template is null) return Result<PipelineTemplateDto>.Failure("PipelineTemplate không tồn tại.");

        if (cmd.IsDefault && !template.IsDefault)
        {
            var existingDefaults = await _dbContext.PipelineTemplates
                .Where(t => t.IsDefault && t.IsActive && t.Id != cmd.Id).ToListAsync(ct);
            foreach (var ed in existingDefaults) ed.UnsetDefault();
        }

        try { template.Update(cmd.Name, cmd.Description, cmd.IsDefault, cmd.IsActive); }
        catch (ArgumentException ex) { return Result<PipelineTemplateDto>.Failure(ex.Message); }

        if (cmd.Stages is not null && cmd.Stages.Count > 0)
        {
            // Replace all stages
            var existing = _dbContext.PipelineTemplateStages.Where(s => s.PipelineTemplateId == template.Id);
            _dbContext.PipelineTemplateStages.RemoveRange(existing);

            foreach (var sr in cmd.Stages.OrderBy(s => s.Order))
            {
                WorkspaceRole? role = sr.RequiredRole is not null
                    ? Enum.Parse<WorkspaceRole>(sr.RequiredRole) : null;
                var stage = PipelineTemplateStage.Create(
                    template.Id, sr.Code, sr.Name, sr.Order, role, sr.GateType, sr.IsRequired, sr.ConfigurationJson);
                _dbContext.PipelineTemplateStages.Add(stage);
            }
        }

        await _dbContext.SaveChangesAsync(ct);

        var loaded = await _dbContext.PipelineTemplates
            .Include(t => t.Stages)
            .FirstAsync(t => t.Id == template.Id, ct);

        return Result<PipelineTemplateDto>.Success(loaded.ToDto());
    }
}
