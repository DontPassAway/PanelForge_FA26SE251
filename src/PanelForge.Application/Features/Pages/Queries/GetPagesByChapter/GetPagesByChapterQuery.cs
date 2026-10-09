using MediatR;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Pages.Models;

namespace PanelForge.Application.Features.Pages.Queries.GetPagesByChapter;

public sealed record GetPagesByChapterQuery(Guid ChapterId)
    : IRequest<Result<IReadOnlyList<PageDto>>>;
