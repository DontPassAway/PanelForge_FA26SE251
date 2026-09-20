using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Chapters.Models;
using PanelForge.Application.Interfaces.Persistence;

namespace PanelForge.Application.Features.Chapters.Commands.UpdateChapter;

public sealed record UpdateChapterCommand(
    Guid ChapterId,
    Guid UserId,
    string? Title,
    DateOnly? TargetReleaseDate,
    bool IncrementVersion
) : IRequest<Result<ChapterDto>>;

public sealed class UpdateChapterCommandHandler : IRequestHandler<UpdateChapterCommand, Result<ChapterDto>>
{
    private readonly IPanelForgeDbContext _dbContext;

    public UpdateChapterCommandHandler(IPanelForgeDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<ChapterDto>> Handle(UpdateChapterCommand command, CancellationToken cancellationToken)
    {
        var chapter = await _dbContext.Chapters
            .Include(c => c.Scenes)
            .Include(c => c.Pages)
            .FirstOrDefaultAsync(c => c.Id == command.ChapterId, cancellationToken);

        if (chapter == null)
            return Result<ChapterDto>.Failure($"Chapter {command.ChapterId} không tồn tại.");

        chapter.UpdateDetails(command.Title, command.TargetReleaseDate);
        chapter.UpdatedBy = command.UserId.ToString();

        if (command.IncrementVersion)
        {
            chapter.IncrementVersion();
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result<ChapterDto>.Success(new ChapterDto(
            Id: chapter.Id,
            SeriesId: chapter.SeriesId,
            ChapterNumber: chapter.ChapterNumber,
            Title: chapter.Title,
            TargetReleaseDate: chapter.TargetReleaseDate,
            VersionVector: chapter.VersionVector,
            CreatedAt: chapter.CreatedAt,
            UpdatedAt: chapter.UpdatedAt,
            CreatedBy: chapter.CreatedBy,
            ScenesCount: chapter.Scenes.Count,
            PagesCount: chapter.Pages.Count
        ));
    }
}
