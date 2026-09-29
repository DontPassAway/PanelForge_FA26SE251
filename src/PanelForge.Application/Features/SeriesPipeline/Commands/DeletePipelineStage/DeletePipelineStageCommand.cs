using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.SeriesPipeline.Models;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Enums;
using PanelForge.Application.Features.SeriesPipeline.Common;

namespace PanelForge.Application.Features.SeriesPipeline.Commands.DeletePipelineStage;

public sealed record DeletePipelineStageCommand(Guid SeriesId, Guid StageId) : IRequest<Result<SeriesPipelineDto>>;

public sealed class DeletePipelineStageCommandHandler : IRequestHandler<DeletePipelineStageCommand, Result<SeriesPipelineDto>>
{
    private readonly IPanelForgeDbContext _dbContext;
    public DeletePipelineStageCommandHandler(IPanelForgeDbContext dbContext) => _dbContext = dbContext;

    public async Task<Result<SeriesPipelineDto>> Handle(DeletePipelineStageCommand cmd, CancellationToken ct)
    {
        var pipeline = await SeriesPipelineStore.LoadAsync(_dbContext, cmd.SeriesId, ct);
        if (pipeline is null) return SeriesPipelineStore.NotFound(cmd.SeriesId);

        if (pipeline.Stages.All(s => s.Id != cmd.StageId))
            return Result<SeriesPipelineDto>.Failure("Stage không thuộc pipeline của Series này.", ResultErrorCodes.NotFound);

        // Không xóa stage đang có Page/Panel nằm ở đó hoặc có Assignment chưa hoàn tất
        var inUse = await _dbContext.Pages.AnyAsync(p => p.CurrentStageId == cmd.StageId, ct)
                 || await _dbContext.Panels.AnyAsync(p => p.CurrentStageId == cmd.StageId, ct)
                 || await _dbContext.Assignments.AnyAsync(
                        a => a.PipelineStageId == cmd.StageId && a.Status != AssignmentStatus.Approved, ct);
        if (inUse)
        {
            return Result<SeriesPipelineDto>.Failure(
                "Không thể xóa stage đang được sử dụng (có Page/Panel đang ở stage này hoặc Assignment chưa hoàn tất). " +
                "Hãy chuyển chúng sang stage khác trước.",
                ResultErrorCodes.Conflict);
        }

        PipelineStage removed;
        try
        {
            removed = pipeline.RemoveStage(cmd.StageId);
        }
        catch (InvalidOperationException ex)
        {
            return Result<SeriesPipelineDto>.Failure(ex.Message);
        }

        // Gỡ transition cũ (đang trỏ tới stage, FK Restrict) TRƯỚC khi xóa stage
        SeriesPipelineStore.RebuildTransitions(_dbContext, pipeline);
        _dbContext.PipelineStages.Remove(removed);
        await _dbContext.SaveChangesAsync(ct);

        return Result<SeriesPipelineDto>.Success(pipeline.ToDto(cmd.SeriesId));
    }
}
