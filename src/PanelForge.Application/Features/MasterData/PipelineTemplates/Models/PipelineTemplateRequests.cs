
namespace PanelForge.Application.Features.MasterData.PipelineTemplates.Models;

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
