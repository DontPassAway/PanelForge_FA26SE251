using MediatR;
using PanelForge.Application.Common;
using PanelForge.Application.DTOs.Content;

namespace PanelForge.Application.Features.Pages.CreatePage;

public sealed record CreatePageCommand(
    Guid ChapterId,
    int? PageNumber = null,
    string? LayoutFormat = "StandardPage",
    int WidthPx = 1200,
    int HeightPx = 1800,
    int Dpi = 300,
    Guid? SceneId = null
) : IRequest<Result<PageDto>>;
