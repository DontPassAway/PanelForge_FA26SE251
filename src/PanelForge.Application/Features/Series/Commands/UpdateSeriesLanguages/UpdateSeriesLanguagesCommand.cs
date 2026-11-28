using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Series.Commands.SetSeriesCover;
using PanelForge.Application.Features.Series.Commands.SetSeriesImage;
using PanelForge.Application.Features.Series.Models;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Series.Commands.UpdateSeriesLanguages;

public sealed record UpdateSeriesLanguagesCommand(
    Guid SeriesId,
    Guid UserId,
    string OriginalLanguage,
    IReadOnlyList<string> ReleaseLanguages
) : IRequest<Result<SeriesDto>>;

public sealed class UpdateSeriesLanguagesCommandHandler
    : IRequestHandler<UpdateSeriesLanguagesCommand, Result<SeriesDto>>
{
    private readonly IPanelForgeDbContext _dbContext;

    public UpdateSeriesLanguagesCommandHandler(IPanelForgeDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<SeriesDto>> Handle(UpdateSeriesLanguagesCommand command, CancellationToken cancellationToken)
    {
        var series = await _dbContext.Series
            .Include(s => s.Chapters)
            .FirstOrDefaultAsync(s => s.Id == command.SeriesId, cancellationToken);

        if (series is null)
            return Result<SeriesDto>.Failure($"Series với Id {command.SeriesId} không tồn tại.");

        // Kiểm tra quyền Producer trong Workspace
        var isProducer = await _dbContext.WorkspaceMembers.AsNoTracking().AnyAsync(m =>
            m.WorkspaceId == series.WorkspaceId &&
            m.UserId == command.UserId &&
            m.Status == WorkspaceMemberStatus.Active &&
            m.Role == WorkspaceRole.Producer, cancellationToken);

        // BR-07: Administrator không cấu hình nội dung sản xuất, kể cả ngôn ngữ Series.
        if (!isProducer)
            return Result<SeriesDto>.Failure("Chỉ Producer của dự án mới có quyền thay đổi cấu hình ngôn ngữ.", ResultErrorCodes.Forbidden);

        // Xác thực ngôn ngữ thuộc ContentLanguages.All
        var normOriginal = ContentLanguages.Normalize(command.OriginalLanguage);
        if (normOriginal is null)
            return Result<SeriesDto>.Failure($"Ngôn ngữ gốc '{command.OriginalLanguage}' không được hệ thống hỗ trợ.", ResultErrorCodes.Validation);

        if (command.ReleaseLanguages is null || command.ReleaseLanguages.Count == 0)
            return Result<SeriesDto>.Failure("Danh sách ngôn ngữ phát hành không được rỗng.", ResultErrorCodes.Validation);

        foreach (var lang in command.ReleaseLanguages)
        {
            if (ContentLanguages.Normalize(lang) is null)
                return Result<SeriesDto>.Failure($"Ngôn ngữ phát hành '{lang}' không được hệ thống hỗ trợ.", ResultErrorCodes.Validation);
        }

        try
        {
            series.SetLanguages(normOriginal, command.ReleaseLanguages);
        }
        catch (ArgumentException ex)
        {
            return Result<SeriesDto>.Failure(ex.Message, ResultErrorCodes.Validation);
        }

        // BR-34: Tự động khởi tạo ChapterEdition cho các chương của Series nếu chưa có
        var chapterIds = series.Chapters.Select(c => c.Id).ToList();
        if (chapterIds.Count > 0)
        {
            var existingEditions = await _dbContext.ChapterEditions
                .Where(e => chapterIds.Contains(e.ChapterId))
                .Select(e => new { e.ChapterId, e.LanguageCode })
                .ToListAsync(cancellationToken);

            var existingSet = existingEditions.Select(e => (e.ChapterId, e.LanguageCode.ToLowerInvariant())).ToHashSet();

            foreach (var chId in chapterIds)
            {
                foreach (var lang in series.ReleaseLanguages)
                {
                    if (!existingSet.Contains((chId, lang.ToLowerInvariant())))
                    {
                        var ed = ChapterEdition.Create(chId, lang, EditionStatus.Draft);
                        ed.CreatedBy = command.UserId.ToString();
                        _dbContext.ChapterEditions.Add(ed);
                    }
                }
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result<SeriesDto>.Success(new SeriesDto(
            Id: series.Id,
            WorkspaceId: series.WorkspaceId,
            Title: series.Title,
            Synopsis: series.Synopsis,
            ReadingDirection: series.ReadingDirection,
            PipelineDefinitionId: series.PipelineDefinitionId,
            Genre: series.Genre,
            Format: series.Format,
            ReleaseScheduleJson: series.ReleaseScheduleJson,
            CreatedAt: series.CreatedAt,
            UpdatedAt: series.UpdatedAt,
            CreatedBy: series.CreatedBy,
            ChaptersCount: series.Chapters.Count,
            StoryArc: series.StoryArc,
            CoverUrl: SetSeriesCoverCommandHandler.CoverUrl(series.Id, series.CoverImageSha256),
            AvatarUrl: SetSeriesImageCommandHandler.ImageUrl(series.Id, SeriesImageKind.Avatar, series.AvatarImageSha256),
            BannerUrl: SetSeriesImageCommandHandler.ImageUrl(series.Id, SeriesImageKind.Banner, series.BannerImageSha256),
            IsCompleted: series.IsCompleted,
            OriginalLanguage: series.OriginalLanguage,
            ReleaseLanguages: series.ReleaseLanguages
        ));
    }
}
