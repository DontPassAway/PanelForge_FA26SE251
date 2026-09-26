using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Chapters.Models;
using PanelForge.Application.Interfaces.Persistence;
using DomainChapter = PanelForge.Domain.Entities.Content.Chapter;

namespace PanelForge.Application.Features.Chapters.Commands.CreateChapter;

public sealed record CreateChapterCommand(
    Guid SeriesId,
    Guid UserId,
    decimal ChapterNumber,
    string? Title = null,
    DateOnly? TargetReleaseDate = null
) : IRequest<Result<ChapterDto>>;

public sealed class CreateChapterCommandHandler : IRequestHandler<CreateChapterCommand, Result<ChapterDto>>
{
    private readonly IPanelForgeDbContext _dbContext;

    public CreateChapterCommandHandler(IPanelForgeDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<ChapterDto>> Handle(CreateChapterCommand command, CancellationToken cancellationToken)
    {
        // 1. Kiểm tra Series có tồn tại không
        var seriesExists = await _dbContext.Series
            .AnyAsync(s => s.Id == command.SeriesId, cancellationToken);

        if (!seriesExists)
            return Result<ChapterDto>.Failure($"Series {command.SeriesId} không tồn tại.");

        // 2. Kiểm tra trùng số chương trong Series
        var isDuplicateNumber = await _dbContext.Chapters
            .AnyAsync(c => c.SeriesId == command.SeriesId && c.ChapterNumber == command.ChapterNumber, cancellationToken);

        if (isDuplicateNumber)
            return Result<ChapterDto>.Failure($"Chương số {command.ChapterNumber} đã tồn tại trong Series này.");

        // 3. Khởi tạo Chapter
        DomainChapter chapter;
        try
        {
            chapter = DomainChapter.Create(
                seriesId: command.SeriesId,
                chapterNumber: command.ChapterNumber,
                title: command.Title,
                targetReleaseDate: command.TargetReleaseDate
            );
            chapter.CreatedBy = command.UserId.ToString();
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return Result<ChapterDto>.Failure(ex.Message);
        }

        _dbContext.Chapters.Add(chapter);
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
            ScenesCount: 0,
            PagesCount: 0,
            Status: chapter.Status,
            PublishedAt: chapter.PublishedAt
        ));
    }
}
