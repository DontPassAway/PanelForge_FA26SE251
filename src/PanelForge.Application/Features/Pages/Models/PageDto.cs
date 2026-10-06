namespace PanelForge.Application.Features.Pages.Models;

public sealed record PageDto(
    Guid Id,
    Guid ChapterId,
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
    Guid? SceneId = null
);

public sealed record ReorderPageRequest(
    int NewPageNumber
);
