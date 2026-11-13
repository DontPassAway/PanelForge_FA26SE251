using MediatR;
using PanelForge.Application.Features.Chapters.Commands.SetChapterCover;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Chapters.Common;
using PanelForge.Application.Features.Chapters.Models;
using PanelForge.Application.Common.Interfaces.Persistence;

namespace PanelForge.Application.Features.Chapters.Commands.UpdateChapter;

public sealed record UpdateChapterCommand(
    Guid ChapterId,
    Guid UserId,
    string? Title,
    DateOnly? TargetReleaseDate,
    bool IncrementVersion,
    string? Outline = null,
    bool? IsLocked = null,
    decimal? PriceVnd = null
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
        if (ChapterEditGuard.ReadOnlyReason(chapter) is { } readOnly)
            return Result<ChapterDto>.Failure(readOnly, ResultErrorCodes.ChapterReadOnly);

        chapter.UpdateDetails(command.Title, command.TargetReleaseDate, command.Outline);
        chapter.UpdatedBy = command.UserId.ToString();

        if (command.IsLocked.HasValue || command.PriceVnd.HasValue)
        {
            chapter.SetPricing(command.IsLocked ?? chapter.IsLocked, command.PriceVnd ?? chapter.PriceVnd);
        }

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
            PagesCount: chapter.Pages.Count,
            Status: chapter.Status,
            PublishedAt: chapter.PublishedAt,
            Outline: chapter.Outline,
            IsScriptFrozen: chapter.IsScriptFrozen,
            ScriptFrozenAt: chapter.ScriptFrozenAt,
            ScriptFrozenById: chapter.ScriptFrozenById,
            CoverUrl: SetChapterCoverCommandHandler.CoverUrl(chapter.Id, chapter.CoverImageSha256),
            ScriptContentHash: chapter.ScriptContentHash,
            IsLocked: chapter.IsLocked,
            PriceVnd: chapter.PriceVnd,
            ScriptStatus: chapter.ScriptStatus,
            ScriptSubmittedAt: chapter.ScriptSubmittedAt
        ));
    }
}
