namespace PanelForge.Application.DTOs.Content;

public sealed record PageDto(
    Guid Id,
    Guid ChapterId,
    Guid? SceneId,
    int PageNumber,
    string LayoutFormat,
    int WidthPx,
    int HeightPx,
    int Dpi,
    int ElementsCount
);

public sealed record PageDetailDto(
    Guid Id,
    Guid ChapterId,
    Guid? SceneId,
    int PageNumber,
    string LayoutFormat,
    int WidthPx,
    int HeightPx,
    int Dpi,
    long Version,
    int ActiveElementsCount
);

public sealed record PageContentHierarchyDto(
    Guid Id,
    Guid ChapterId,
    Guid? SceneId,
    int PageNumber,
    string LayoutFormat,
    int WidthPx,
    int HeightPx,
    int Dpi,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    IReadOnlyList<PagePanelDetailDto> Panels
);

public sealed record PagePanelDetailDto(
    Guid Id,
    Guid PageId,
    int PanelNumber,
    string BoundingBox,
    int ReadingOrder,
    Guid? CurrentStageId,
    DateTime CreatedAt,
    IReadOnlyList<PageElementDetailDto> Elements
);

public sealed record PageElementDetailDto(
    Guid Id,
    Guid PanelId,
    string ElementType,
    int ZIndex,
    string TransformGeometry,
    string? Content,
    string StyleProperties,
    Guid? ScriptLineId,
    Guid? SpeakerCharacterId,
    DateTime CreatedAt
);

public sealed record ReorderPageRequest(
    int NewPageNumber
);
