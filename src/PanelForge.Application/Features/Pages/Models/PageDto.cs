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
