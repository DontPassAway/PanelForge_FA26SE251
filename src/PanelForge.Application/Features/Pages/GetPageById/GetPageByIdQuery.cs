using MediatR;
using PanelForge.Application.Common;
using PanelForge.Application.DTOs.Content;

namespace PanelForge.Application.Features.Pages.GetPageById;

public sealed record GetPageByIdQuery(Guid PageId)
    : IRequest<Result<PageDetailDto>>;
