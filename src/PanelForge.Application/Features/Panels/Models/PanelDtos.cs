namespace PanelForge.Application.Features.Panels.Models;

public sealed record PanelDto(
    Guid Id,
    Guid PageId,
    int PanelNumber,
    string BoundingBox,
    int ReadingOrder,
    Guid? CurrentStageId,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    string? CreatedBy
);

public sealed record PanelDetailDto(
    Guid Id,
    Guid PageId,
    int PanelNumber,
    string BoundingBox,
    int ReadingOrder,
    Guid? CurrentStageId,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    string? CreatedBy
);

public sealed record CreatePanelRequest(
    int? PanelNumber = null,
    int? ReadingOrder = null,
    string? BoundingBox = null
);

public sealed record UpdatePanelCoordinatesRequest(
    string BoundingBox,
    int? ReadingOrder = null
);
