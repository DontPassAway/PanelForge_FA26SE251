using MediatR;
using PanelForge.Application.Common;

namespace PanelForge.Application.Features.Pages.UpdateCanvasSettings;

public sealed record UpdateCanvasSettingsCommand(
    Guid PageId,
    int WidthPx,
    int HeightPx,
    int Dpi,
    string LayoutFormat
) : IRequest<Result<bool>>;
