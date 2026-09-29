using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.SeriesPipeline.Models;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Domain.Enums;
using PanelForge.Application.Features.SeriesPipeline.Common;

namespace PanelForge.Application.Features.SeriesPipeline.Commands.UpdatePipelineStage;

public sealed record UpdatePipelineStageCommand(
    Guid SeriesId,
    Guid StageId,
    string Name,
    string? ColorCode,
    WorkspaceRole? AllowedRole,
    bool IsApprovalGate,
    int? EstimatedDurationDays
) : IRequest<Result<SeriesPipelineDto>>;

public sealed class UpdatePipelineStageCommandHandler : IRequestHandler<UpdatePipelineStageCommand, Result<SeriesPipelineDto>>
{
    private readonly IPanelForgeDbContext _dbContext;
    public UpdatePipelineStageCommandHandler(IPanelForgeDbContext dbContext) => _dbContext = dbContext;

    public async Task<Result<SeriesPipelineDto>> Handle(UpdatePipelineStageCommand cmd, CancellationToken ct)
    {
        var pipeline = await SeriesPipelineStore.LoadAsync(_dbContext, cmd.SeriesId, ct);
        if (pipeline is null) return SeriesPipelineStore.NotFound(cmd.SeriesId);

        var stage = pipeline.Stages.FirstOrDefault(s => s.Id == cmd.StageId);
        if (stage is null)
            return Result<SeriesPipelineDto>.Failure("Stage không thuộc pipeline của Series này.", ResultErrorCodes.NotFound);

        if (cmd.EstimatedDurationDays is < 0)
            return Result<SeriesPipelineDto>.Failure("Số ngày ước tính không được âm.");

        try
        {
            stage.UpdateDetails(cmd.Name, cmd.ColorCode, cmd.AllowedRole, cmd.IsApprovalGate, cmd.EstimatedDurationDays);
        }
        catch (ArgumentException ex)
        {
            return Result<SeriesPipelineDto>.Failure(ex.Message);
        }

        // Vai trò / cổng duyệt thay đổi thì RequiredRole và nhánh reject của transition cũng đổi theo
        SeriesPipelineStore.RebuildTransitions(_dbContext, pipeline);
        await _dbContext.SaveChangesAsync(ct);

        return Result<SeriesPipelineDto>.Success(pipeline.ToDto(cmd.SeriesId));
    }
}
