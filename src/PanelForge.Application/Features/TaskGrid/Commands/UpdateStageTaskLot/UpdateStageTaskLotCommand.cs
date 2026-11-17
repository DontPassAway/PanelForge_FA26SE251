using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Assignments.Common;
using PanelForge.Application.Features.TaskGrid.Models;

namespace PanelForge.Application.Features.TaskGrid.Commands.UpdateStageTaskLot;

/// <summary>Producer sửa brief / hạn của lô (gia hạn khi trễ). Brief null = giữ nguyên; ClearDueDate = bỏ hạn.</summary>
public sealed record UpdateStageTaskLotCommand(Guid ChapterId, Guid LotId, Guid UserId, string? Brief, DateTime? DueDate, bool ClearDueDate)
    : IRequest<Result<StageTaskLotDto>>, ITransactionalCommand;

public sealed class UpdateStageTaskLotCommandHandler : IRequestHandler<UpdateStageTaskLotCommand, Result<StageTaskLotDto>>
{
    private readonly IPanelForgeDbContext _db;
    public UpdateStageTaskLotCommandHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<StageTaskLotDto>> Handle(UpdateStageTaskLotCommand cmd, CancellationToken ct)
    {
        static Result<StageTaskLotDto> Fail(string msg, string code) => Result<StageTaskLotDto>.Failure(msg, code);

        var chapter = await _db.Chapters.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cmd.ChapterId, ct);
        if (chapter is null) return Fail($"Chapter {cmd.ChapterId} không tồn tại.", ResultErrorCodes.NotFound);
        if (AssignmentRules.ChapterNotEditableReason(chapter) is { } locked) return Fail(locked, ResultErrorCodes.Conflict);

        var lot = await _db.StageTaskLots.FirstOrDefaultAsync(l => l.Id == cmd.LotId && l.ChapterId == chapter.Id, ct);
        if (lot is null) return Fail($"Lô {cmd.LotId} không thuộc chương này.", ResultErrorCodes.NotFound);

        var dueDate = cmd.ClearDueDate ? null : cmd.DueDate ?? lot.DueDate;
        if (dueDate is { } due && dueDate != lot.DueDate && due < DateTime.UtcNow)
            return Fail("Hạn mới phải ở tương lai.", ResultErrorCodes.Validation);

        try { lot.UpdateDetails(cmd.Brief ?? lot.Brief, dueDate); }
        catch (ArgumentException ex) { return Fail(ex.Message, ResultErrorCodes.Validation); }
        catch (InvalidOperationException ex) { return Fail(ex.Message, ResultErrorCodes.Conflict); }

        lot.UpdatedBy = cmd.UserId.ToString();
        await _db.SaveChangesAsync(ct);
        var tasks = await _db.StageTasks.AsNoTracking().Where(t => t.LotId == lot.Id).ToListAsync(ct);
        return Result<StageTaskLotDto>.Success(StageTaskLotDto.From(lot, tasks, DateTime.UtcNow));
    }
}
