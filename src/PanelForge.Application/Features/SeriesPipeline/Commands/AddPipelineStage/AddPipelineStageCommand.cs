using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.SeriesPipeline.Models;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Enums;
using PanelForge.Application.Features.SeriesPipeline.Common;

namespace PanelForge.Application.Features.SeriesPipeline.Commands.AddPipelineStage;

public sealed record AddPipelineStageCommand(
    Guid SeriesId,
    string Name,
    string? Slug,
    int? Position,
    string? ColorCode,
    WorkspaceRole? AllowedRole,
    bool IsApprovalGate,
    int? EstimatedDurationDays
) : IRequest<Result<SeriesPipelineDto>>;

public sealed class AddPipelineStageCommandHandler : IRequestHandler<AddPipelineStageCommand, Result<SeriesPipelineDto>>
{
    private readonly IPanelForgeDbContext _dbContext;
    public AddPipelineStageCommandHandler(IPanelForgeDbContext dbContext) => _dbContext = dbContext;

    public async Task<Result<SeriesPipelineDto>> Handle(AddPipelineStageCommand cmd, CancellationToken ct)
    {
        var pipeline = await SeriesPipelineStore.LoadAsync(_dbContext, cmd.SeriesId, ct);
        if (pipeline is null) return SeriesPipelineStore.NotFound(cmd.SeriesId);

        if (cmd.EstimatedDurationDays is < 0)
            return Result<SeriesPipelineDto>.Failure("Số ngày ước tính không được âm.");

        PipelineStage stage;
        try
        {
            stage = pipeline.InsertStage(
                cmd.Name, cmd.Slug, cmd.Position, cmd.ColorCode, cmd.AllowedRole,
                cmd.IsApprovalGate, cmd.EstimatedDurationDays);
        }
        catch (ArgumentException ex)
        {
            return Result<SeriesPipelineDto>.Failure(ex.Message);
        }

        _dbContext.PipelineStages.Add(stage);
        SeriesPipelineStore.RebuildTransitions(_dbContext, pipeline);
        await _dbContext.SaveChangesAsync(ct);

        return Result<SeriesPipelineDto>.Success(pipeline.ToDto(cmd.SeriesId));
    }
}
