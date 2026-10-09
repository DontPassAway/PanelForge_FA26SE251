using PanelForge.Domain.Entities.MasterData;
using PanelForge.Application.Features.MasterData.PipelineTemplates.Models;

namespace PanelForge.Application.Features.MasterData.PipelineTemplates.Common;

public static class PipelineTemplateExtensions
{
    public static PipelineTemplateStageDto ToDto(this PipelineTemplateStage s) => new(
        s.Id, s.Code, s.Name, s.Order, s.RequiredRole?.ToString(), s.GateType, s.IsRequired, s.ConfigurationJson);

    public static PipelineTemplateDto ToDto(this PipelineTemplate t) => new(
        t.Id, t.Code, t.Name, t.Description, t.IsDefault, t.IsActive,
        t.Stages.OrderBy(s => s.Order).Select(s => s.ToDto()).ToList(),
        t.CreatedAt, t.UpdatedAt);
}
