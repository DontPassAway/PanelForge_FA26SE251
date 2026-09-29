
namespace PanelForge.Application.Features.MasterData.PipelineTemplates.Models;

public sealed record PipelineTemplateStageDto(
    Guid Id, string Code, string Name, int Order,
    string? RequiredRole, string? GateType, bool IsRequired, string ConfigurationJson);

public sealed record PipelineTemplateDto(
    Guid Id, string Code, string Name, string? Description,
    bool IsDefault, bool IsActive,
    IReadOnlyList<PipelineTemplateStageDto> Stages,
    DateTime CreatedAt, DateTime? UpdatedAt);
