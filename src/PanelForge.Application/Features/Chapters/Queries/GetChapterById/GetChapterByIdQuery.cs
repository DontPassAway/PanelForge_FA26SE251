using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Chapters.Common;
using PanelForge.Application.Features.Chapters.Models;

namespace PanelForge.Application.Features.Chapters.Queries.GetChapterById;

public sealed record GetChapterByIdQuery(Guid ChapterId) : IRequest<Result<ChapterDetailDto>>;

public sealed class GetChapterByIdQueryHandler : IRequestHandler<GetChapterByIdQuery, Result<ChapterDetailDto>>
{
    private readonly IPanelForgeDbContext _dbContext;

    public GetChapterByIdQueryHandler(IPanelForgeDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<ChapterDetailDto>> Handle(GetChapterByIdQuery query, CancellationToken cancellationToken)
    {
        var chapter = await _dbContext.Chapters
            .AsNoTracking()
            .Include(c => c.Scenes)
            .Include(c => c.Pages)
            .FirstOrDefaultAsync(c => c.Id == query.ChapterId, cancellationToken);

        if (chapter == null)
            return Result<ChapterDetailDto>.Failure($"Chapter {query.ChapterId} không tồn tại.");

        return Result<ChapterDetailDto>.Success(ChapterDetailDtoFactory.Create(chapter));
    }
}
