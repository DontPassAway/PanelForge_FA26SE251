using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.ContentElements.Models;

public sealed record ContentElementDto(
    Guid Id,
    Guid PanelId,
    ElementType ElementType,
    int ZIndex,
    string TransformGeometry,
    string? Content,
    string StyleProperties,
    Guid? ScriptLineId,
    Guid? SpeakerCharacterId,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    string? CreatedBy
);

public sealed record CreateContentElementRequest(
    ElementType ElementType,
    int ZIndex = 0,
    string? Content = null,
    string? TransformGeometry = null,
    string? StyleProperties = null,
    Guid? ScriptLineId = null,
    Guid? SpeakerCharacterId = null
);

public sealed record UpdateContentElementRequest(
    string? Content,
    string? TransformGeometry = null,
    string? StyleProperties = null,
    int? ZIndex = null,
    Guid? ScriptLineId = null,
    Guid? SpeakerCharacterId = null
);
