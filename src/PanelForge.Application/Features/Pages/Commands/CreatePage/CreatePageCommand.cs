using MediatR;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Pages.Models;

namespace PanelForge.Application.Features.Pages.Commands.CreatePage;

public sealed record CreatePageCommand(
    Guid ChapterId,
    int? PageNumber = null,
    string? LayoutFormat = "StandardPage",
    int WidthPx = 1200,
    int HeightPx = 1800,
    int Dpi = 300,
    Guid? UserId = null
) : IRequest<Result<PageDto>>;
