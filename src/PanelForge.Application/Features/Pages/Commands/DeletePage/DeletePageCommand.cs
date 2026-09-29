using MediatR;
using PanelForge.Application.Common;

namespace PanelForge.Application.Features.Pages.Commands.DeletePage;

public sealed record DeletePageCommand(Guid PageId)
    : IRequest<Result<bool>>;
