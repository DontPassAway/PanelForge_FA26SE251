using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.SeriesPipeline.Models;
using PanelForge.Application.Interfaces.Persistence;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.SeriesPipeline;

// UC-03 bước 3: Producer cấu hình pipeline stage và vai trò của từng stage cho Series.
// Pipeline của Series là bản sao độc lập của PipelineTemplate (CreateSeriesCommand), nên sửa ở đây
// không ảnh hưởng template của Admin hay Series khác. Quyền Producer được kiểm tra ở RequireWorkspaceRole.

internal static class SeriesPipelineStore
{
    public static async Task<PipelineDefinition?> LoadAsync(IPanelForgeDbContext db, Guid seriesId, CancellationToken ct)
    {
        var pipelineId = await db.Series
            .Where(s => s.Id == seriesId)
            .Select(s => s.PipelineDefinitionId)
            .FirstOrDefaultAsync(ct);

        if (pipelineId is null) return null;

        return await db.PipelineDefinitions
            .Include(p => p.Stages)
            .Include(p => p.Transitions)
            .FirstOrDefaultAsync(p => p.Id == pipelineId.Value, ct);
    }

    public static Result<SeriesPipelineDto> NotFound(Guid seriesId)
        => Result<SeriesPipelineDto>.Failure($"Series {seriesId} không tồn tại hoặc chưa có pipeline.", ResultErrorCodes.NotFound);

    /// <summary>Dựng lại transition theo thứ tự stage mới và đồng bộ sang DbContext.</summary>
    public static void RebuildTransitions(IPanelForgeDbContext db, PipelineDefinition pipeline)
    {
        var (removed, added) = pipeline.RebuildDefaultTransitions();
        foreach (var t in removed) db.StageTransitions.Remove(t);
        foreach (var t in added) db.StageTransitions.Add(t);
    }
}

// ─── Get ──────────────────────────────────────────────────────────────────────

public sealed record GetSeriesPipelineQuery(Guid SeriesId) : IRequest<Result<SeriesPipelineDto>>;

public sealed class GetSeriesPipelineQueryHandler : IRequestHandler<GetSeriesPipelineQuery, Result<SeriesPipelineDto>>
{
    private readonly IPanelForgeDbContext _dbContext;
    public GetSeriesPipelineQueryHandler(IPanelForgeDbContext dbContext) => _dbContext = dbContext;

    public async Task<Result<SeriesPipelineDto>> Handle(GetSeriesPipelineQuery query, CancellationToken ct)
    {
        var pipeline = await SeriesPipelineStore.LoadAsync(_dbContext, query.SeriesId, ct);
        return pipeline is null
            ? SeriesPipelineStore.NotFound(query.SeriesId)
            : Result<SeriesPipelineDto>.Success(pipeline.ToDto(query.SeriesId));
    }
}

// ─── Add stage ────────────────────────────────────────────────────────────────

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

// ─── Update stage ─────────────────────────────────────────────────────────────

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

// ─── Reorder ──────────────────────────────────────────────────────────────────

public sealed record ReorderPipelineStagesCommand(Guid SeriesId, IReadOnlyList<Guid> StageIds)
    : IRequest<Result<SeriesPipelineDto>>;

public sealed class ReorderPipelineStagesCommandHandler : IRequestHandler<ReorderPipelineStagesCommand, Result<SeriesPipelineDto>>
{
    private readonly IPanelForgeDbContext _dbContext;
    public ReorderPipelineStagesCommandHandler(IPanelForgeDbContext dbContext) => _dbContext = dbContext;

    public async Task<Result<SeriesPipelineDto>> Handle(ReorderPipelineStagesCommand cmd, CancellationToken ct)
    {
        var pipeline = await SeriesPipelineStore.LoadAsync(_dbContext, cmd.SeriesId, ct);
        if (pipeline is null) return SeriesPipelineStore.NotFound(cmd.SeriesId);

        try
        {
            pipeline.ReorderStages(cmd.StageIds ?? []);
        }
        catch (ArgumentException ex)
        {
            return Result<SeriesPipelineDto>.Failure(ex.Message);
        }

        SeriesPipelineStore.RebuildTransitions(_dbContext, pipeline);
        await _dbContext.SaveChangesAsync(ct);

        return Result<SeriesPipelineDto>.Success(pipeline.ToDto(cmd.SeriesId));
    }
}

// ─── Delete stage ─────────────────────────────────────────────────────────────

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
