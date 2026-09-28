using MediatR;
using PanelForge.Application.Common;
using PanelForge.Application.DTOs.Content;

namespace PanelForge.Application.Features.Pages.GetPagesByChapter;

public sealed record GetPagesByChapterQuery(Guid ChapterId)
    : IRequest<Result<IReadOnlyList<PageDto>>>;
