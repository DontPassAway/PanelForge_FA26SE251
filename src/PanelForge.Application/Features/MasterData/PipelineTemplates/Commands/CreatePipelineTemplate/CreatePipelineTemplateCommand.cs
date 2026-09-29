using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Domain.Entities.MasterData;
using PanelForge.Domain.Enums;
using PanelForge.Application.Features.MasterData.PipelineTemplates.Models;
using PanelForge.Application.Features.MasterData.PipelineTemplates.Common;

namespace PanelForge.Application.Features.MasterData.PipelineTemplates.Commands.CreatePipelineTemplate;

public sealed record CreatePipelineTemplateCommand(
    string Code, string Name, string? Description, bool IsDefault,
    IReadOnlyList<PipelineTemplateStageRequest> Stages
) : IRequest<Result<PipelineTemplateDto>>;

public sealed class CreatePipelineTemplateCommandHandler
    : IRequestHandler<CreatePipelineTemplateCommand, Result<PipelineTemplateDto>>
{
    private readonly IPanelForgeDbContext _dbContext;
    public CreatePipelineTemplateCommandHandler(IPanelForgeDbContext dbContext) => _dbContext = dbContext;

    public async Task<Result<PipelineTemplateDto>> Handle(CreatePipelineTemplateCommand cmd, CancellationToken ct)
    {
        var code = cmd.Code.Trim().ToUpperInvariant();
        if (await _dbContext.PipelineTemplates.AnyAsync(t => t.Code == code, ct))
            return Result<PipelineTemplateDto>.Failure($"PipelineTemplate với Code '{code}' đã tồn tại.");

        if (!cmd.Stages.Any())
            return Result<PipelineTemplateDto>.Failure("PipelineTemplate phải có ít nhất một stage.");

        var orders = cmd.Stages.Select(s => s.Order).ToList();
        if (orders.Distinct().Count() != orders.Count)
            return Result<PipelineTemplateDto>.Failure("Stage Order phải là duy nhất trong template.");

        // If this is marked as default, unset any existing default
        if (cmd.IsDefault)
        {
            var existingDefaults = await _dbContext.PipelineTemplates
                .Where(t => t.IsDefault && t.IsActive).ToListAsync(ct);
            foreach (var ed in existingDefaults) ed.UnsetDefault();
        }

        PipelineTemplate template;
        try { template = PipelineTemplate.Create(cmd.Code, cmd.Name, cmd.Description, cmd.IsDefault); }
        catch (ArgumentException ex) { return Result<PipelineTemplateDto>.Failure(ex.Message); }

        _dbContext.PipelineTemplates.Add(template);

        foreach (var sr in cmd.Stages.OrderBy(s => s.Order))
        {
            WorkspaceRole? role = sr.RequiredRole is not null
                ? Enum.Parse<WorkspaceRole>(sr.RequiredRole) : null;

            var stage = PipelineTemplateStage.Create(
                template.Id, sr.Code, sr.Name, sr.Order, role, sr.GateType, sr.IsRequired, sr.ConfigurationJson);
            _dbContext.PipelineTemplateStages.Add(stage);
        }

        await _dbContext.SaveChangesAsync(ct);

        // Reload with stages
        var loaded = await _dbContext.PipelineTemplates
            .Include(t => t.Stages)
            .FirstAsync(t => t.Id == template.Id, ct);

        return Result<PipelineTemplateDto>.Success(loaded.ToDto());
    }
}
