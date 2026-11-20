using PanelForge.Application.Features.Series.Commands.SetSeriesCover;
using PanelForge.Application.Features.Series.Commands.SetSeriesImage;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Series.Models;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Domain.Entities.Bible;
using DomainSeries = PanelForge.Domain.Entities.Content.Series;
using PanelForge.Domain.Enums;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Entities.MasterData;

using PanelForge.Application.Features.Series.Common;

namespace PanelForge.Application.Features.Series.Commands.CreateSeries;

public sealed record CreateSeriesCommand(
    Guid WorkspaceId,
    Guid UserId,
    string Title,
    string Format = "MANGA",
    ReadingDirection? ReadingDirection = null,
    string? Synopsis = null,
    string? Genre = null,
    string? StoryArc = null,
    Guid? PipelineTemplateId = null
) : IRequest<Result<SeriesDto>>;

/// <summary>
/// Lightweight CreateSeriesCommandHandler (UC-03).
/// Accepts minimal input (Title + Format) or full initial setup (ReadingDirection, Genre, Synopsis, StoryArc):
/// - Defaults reading direction to RTL for Manga, Vertical for Webtoon if not specified.
/// - Clones active or requested PipelineTemplate.
/// - Initializes empty Series Bible v1.0.
/// </summary>
public sealed class CreateSeriesCommandHandler : IRequestHandler<CreateSeriesCommand, Result<SeriesDto>>
{
    private readonly IPanelForgeDbContext _dbContext;

    public CreateSeriesCommandHandler(IPanelForgeDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<SeriesDto>> Handle(CreateSeriesCommand command, CancellationToken cancellationToken)
    {
        // 1. Kiểm tra Workspace có tồn tại không
        var workspaceExists = await _dbContext.StudioWorkspaces
            .AnyAsync(w => w.Id == command.WorkspaceId, cancellationToken);

        if (!workspaceExists)
            return Result<SeriesDto>.Failure($"Workspace {command.WorkspaceId} không tồn tại.");

        // 2. Kiểm tra User có phải thành viên Workspace và có vai trò Producer (UC-03)
        var member = await _dbContext.WorkspaceMembers
            .FirstOrDefaultAsync(m => m.WorkspaceId == command.WorkspaceId && m.UserId == command.UserId, cancellationToken);
        if (member is null)
            return Result<SeriesDto>.Failure("Bạn không phải thành viên của Workspace này.");

        if (member.Role != WorkspaceRole.Producer)
            return Result<SeriesDto>.Failure("Chỉ người dùng có vai trò Producer mới có quyền tạo Series trong Workspace (UC-03).");

        // 3. Tự động xác định Hướng đọc (ReadingDirection) dựa trên Format nếu không được chỉ định
        var formatUpper = (command.Format ?? "MANGA").Trim().ToUpperInvariant();
        var readingDir = command.ReadingDirection ?? (formatUpper.Contains("MANGA")
            ? ReadingDirection.RightToLeft
            : ReadingDirection.Vertical);

        // 4. Tạo Series entity
        DomainSeries series;
        try
        {
            series = DomainSeries.Create(
                workspaceId: command.WorkspaceId,
                title: command.Title,
                readingDirection: readingDir,
                synopsis: string.IsNullOrWhiteSpace(command.Synopsis) ? null : command.Synopsis.Trim(),
                genre: string.IsNullOrWhiteSpace(command.Genre) ? "Shonen Manga" : command.Genre.Trim(),
                format: command.Format ?? "B5 Tankōbon (600 DPI)",
                releaseScheduleJson: null
            );

            if (!string.IsNullOrWhiteSpace(command.StoryArc))
            {
                series.SetStoryArc(command.StoryArc.Trim());
            }

            series.CreatedBy = command.UserId.ToString();
        }
        catch (ArgumentException ex)
        {
            return Result<SeriesDto>.Failure(ex.Message);
        }

        // 5. Khởi tạo PipelineDefinition (8 công đoạn sản xuất)
        PipelineTemplate? template = null;
        if (command.PipelineTemplateId.HasValue)
        {
            template = await _dbContext.PipelineTemplates
                .Include(t => t.Stages)
                .FirstOrDefaultAsync(t => t.Id == command.PipelineTemplateId.Value && t.IsActive, cancellationToken);
        }

        if (template is null)
        {
            template = await _dbContext.PipelineTemplates
                .Include(t => t.Stages)
                .FirstOrDefaultAsync(t => t.IsDefault && t.IsActive, cancellationToken);
        }

        var pipeline = SeriesPipelineFactory.Create(command.WorkspaceId, series.Id, series.Title, formatUpper, template);

        series.SetPipelineDefinition(pipeline.Id);
        _dbContext.PipelineDefinitions.Add(pipeline);

        // 6. Khởi tạo SeriesBible v1.0 cho bộ truyện
        var bible = SeriesBible.Create(series.Id, command.WorkspaceId, $"{series.Title} - Bible", "Initialized empty Series Bible v1.0");
        series.AttachBible(bible);

        _dbContext.Series.Add(series);
        _dbContext.SeriesBibles.Add(bible);

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
            ChaptersCount: 0,
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
