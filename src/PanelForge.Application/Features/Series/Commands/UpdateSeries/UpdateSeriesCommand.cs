using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Series.Models;
using PanelForge.Application.Interfaces.Persistence;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Series.Commands.UpdateSeries;

public sealed record UpdateSeriesCommand(
    Guid SeriesId,
    Guid UserId,
    string Title,
    string? Synopsis,
    ReadingDirection ReadingDirection,
    Guid? PipelineDefinitionId
) : IRequest<Result<SeriesDto>>;

public sealed class UpdateSeriesCommandHandler : IRequestHandler<UpdateSeriesCommand, Result<SeriesDto>>
{
    private readonly IPanelForgeDbContext _dbContext;

    public UpdateSeriesCommandHandler(IPanelForgeDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<SeriesDto>> Handle(UpdateSeriesCommand command, CancellationToken cancellationToken)
    {
        var series = await _dbContext.Series
            .Include(s => s.Chapters)
            .FirstOrDefaultAsync(s => s.Id == command.SeriesId, cancellationToken);

        if (series == null)
            return Result<SeriesDto>.Failure($"Series {command.SeriesId} không tồn tại.");

        try
        {
            series.UpdateDetails(command.Title, command.Synopsis, command.ReadingDirection);
            series.SetPipelineDefinition(command.PipelineDefinitionId);
            series.UpdatedBy = command.UserId.ToString();
        }
        catch (ArgumentException ex)
        {
            return Result<SeriesDto>.Failure(ex.Message);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result<SeriesDto>.Success(new SeriesDto(
            Id: series.Id,
            WorkspaceId: series.WorkspaceId,
            Title: series.Title,
            Synopsis: series.Synopsis,
            ReadingDirection: series.ReadingDirection,
            PipelineDefinitionId: series.PipelineDefinitionId,
            CreatedAt: series.CreatedAt,
            UpdatedAt: series.UpdatedAt,
            CreatedBy: series.CreatedBy,
            ChaptersCount: series.Chapters.Count
        ));
    }
}
