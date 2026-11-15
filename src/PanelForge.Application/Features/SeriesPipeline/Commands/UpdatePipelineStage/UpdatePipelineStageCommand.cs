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
    int? EstimatedDurationDays,
    bool? IsEnabled = null,
    string? Track = null
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

        // Lưới V3: đổi nhánh / vai trò cần lưới không còn ô dở; tắt stage cần stage đó chưa có ô được giao
        var trackChanged = cmd.Track is not null &&
            !string.Equals(string.IsNullOrWhiteSpace(cmd.Track) ? null : cmd.Track.Trim().ToUpperInvariant(), stage.Track);
        if ((trackChanged || cmd.AllowedRole != stage.AllowedRole) &&
            await SeriesPipelineStore.StructureLockedReasonAsync(_dbContext, cmd.SeriesId, ct) is { } locked)
            return Result<SeriesPipelineDto>.Failure(locked, ResultErrorCodes.Conflict);
        if (cmd.IsEnabled == false && stage.IsEnabled &&
            await SeriesPipelineStore.StageInUseOnGridReasonAsync(_dbContext, stage.Id, ct) is { } inUse)
            return Result<SeriesPipelineDto>.Failure(inUse, ResultErrorCodes.Conflict);

        try
        {
            stage.UpdateDetails(cmd.Name, cmd.ColorCode, cmd.AllowedRole, cmd.IsApprovalGate, cmd.EstimatedDurationDays);
            if (cmd.IsEnabled is { } enabled) stage.SetEnabled(enabled);
            if (cmd.Track is not null) stage.SetTrack(cmd.Track);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return Result<SeriesPipelineDto>.Failure(ex.Message);
        }

        // Tắt stage: huỷ luôn các ô chưa giao của nó, nếu không cổng duyệt chương đếm chúng là "chưa duyệt" và kẹt mãi.
        // Bật lại thì lần "Sinh / Đồng bộ ô" sau hồi sinh các ô này.
        if (cmd.IsEnabled == false)
            foreach (var cell in await _dbContext.StageTasks
                         .Where(t => t.PipelineStageId == stage.Id && t.Status == StageTaskStatus.Unassigned).ToListAsync(ct))
                cell.Cancel();

        // Vai trò / cổng duyệt thay đổi thì RequiredRole và nhánh reject của transition cũng đổi theo
        SeriesPipelineStore.RebuildTransitions(_dbContext, pipeline);
        await _dbContext.SaveChangesAsync(ct);

        return Result<SeriesPipelineDto>.Success(pipeline.ToDto(cmd.SeriesId));
    }
}
