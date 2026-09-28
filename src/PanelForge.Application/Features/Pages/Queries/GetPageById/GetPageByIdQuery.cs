using MediatR;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Pages.Models;

namespace PanelForge.Application.Features.Pages.Queries.GetPageById;

public sealed record GetPageByIdQuery(Guid PageId)
    : IRequest<Result<PageDetailDto>>;
