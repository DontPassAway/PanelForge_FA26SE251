using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Series.Models;
using PanelForge.Application.Interfaces.Persistence;
using PanelForge.Domain.Entities.Bible;
using DomainSeries = PanelForge.Domain.Entities.Content.Series;
using PanelForge.Domain.Enums;

using PanelForge.Domain.Entities.Content;

namespace PanelForge.Application.Features.Series.Commands.CreateSeries;

public sealed record CreateSeriesCommand(
    Guid WorkspaceId,
    Guid UserId,
    string Title,
    string? Synopsis,
    ReadingDirection ReadingDirection,
    Guid? PipelineDefinitionId,
    string Genre = "Action",
    string Format = "Manga",
    string? ReleaseScheduleJson = null
) : IRequest<Result<SeriesDto>>;

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

        // 2. Tạo Series entity (người tạo được lưu vào CreatedBy)
        DomainSeries series;
        try
        {
            series = DomainSeries.Create(
                workspaceId: command.WorkspaceId,
                title: command.Title,
                readingDirection: command.ReadingDirection,
                synopsis: command.Synopsis,
                genre: command.Genre,
                format: command.Format,
                releaseScheduleJson: command.ReleaseScheduleJson
            );

            series.CreatedBy = command.UserId.ToString();

            if (command.PipelineDefinitionId.HasValue)
            {
                series.SetPipelineDefinition(command.PipelineDefinitionId.Value);
            }
        }
        catch (ArgumentException ex)
        {
            return Result<SeriesDto>.Failure(ex.Message);
        }

        // 3. Khởi tạo mặc định SeriesBible cho bộ truyện mới (CF1 Step 6)
        var bible = SeriesBible.Create(series.Id, command.WorkspaceId, $"{series.Title} - Bible");
        series.AttachBible(bible);

        // 4. Khởi tạo mặc định Typography Presets & Consistency Rules (CF1 Step 7, NFR-08)
        var preset = SeriesPreset.Create(series.Id);
        series.AttachPreset(preset);

        _dbContext.Series.Add(series);
        _dbContext.SeriesBibles.Add(bible);
        _dbContext.SeriesPresets?.Add(preset);

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
            ChaptersCount: 0
        ));
    }
}
