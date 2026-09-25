using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Interfaces.Persistence;
using PanelForge.Domain.Entities.MasterData;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.MasterData.PipelineTemplates.Commands;

// ─── DTOs ─────────────────────────────────────────────────────────────────────

public sealed record PipelineTemplateStageDto(
    Guid Id, string Code, string Name, int Order,
    string? RequiredRole, string? GateType, bool IsRequired, string ConfigurationJson);

public sealed record PipelineTemplateDto(
    Guid Id, string Code, string Name, string? Description,
    bool IsDefault, bool IsActive,
    IReadOnlyList<PipelineTemplateStageDto> Stages,
    DateTime CreatedAt, DateTime? UpdatedAt);

public static class PipelineTemplateExtensions
{
    public static PipelineTemplateStageDto ToDto(this PipelineTemplateStage s) => new(
        s.Id, s.Code, s.Name, s.Order, s.RequiredRole?.ToString(), s.GateType, s.IsRequired, s.ConfigurationJson);

    public static PipelineTemplateDto ToDto(this PipelineTemplate t) => new(
        t.Id, t.Code, t.Name, t.Description, t.IsDefault, t.IsActive,
        t.Stages.OrderBy(s => s.Order).Select(s => s.ToDto()).ToList(),
        t.CreatedAt, t.UpdatedAt);
}

// ─── HTTP Request Models ───────────────────────────────────────────────────────

public sealed record PipelineTemplateStageRequest(
    string Code, string Name, int Order,
    string? RequiredRole = null,
    string? GateType = null,
    bool IsRequired = true,
    string? ConfigurationJson = null);

public sealed record CreatePipelineTemplateRequest(
    string Code, string Name, string? Description, bool IsDefault,
    IReadOnlyList<PipelineTemplateStageRequest> Stages);

public sealed record UpdatePipelineTemplateRequest(
    string Name, string? Description, bool IsDefault, bool IsActive,
    IReadOnlyList<PipelineTemplateStageRequest>? Stages);

// ─── Create ───────────────────────────────────────────────────────────────────

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

// ─── Update ───────────────────────────────────────────────────────────────────

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

// ─── Deactivate ───────────────────────────────────────────────────────────────

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
