using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Assignments.Common;
using PanelForge.Application.Features.TaskGrid.Common;
using PanelForge.Application.Features.TaskGrid.Models;

namespace PanelForge.Application.Features.TaskGrid.Commands.WithdrawStageTask;

/// <summary>
/// Người giữ ô rút bài đã nộp về để sửa tiếp, khi Editor chưa mở duyệt (Submitted → InProgress).
/// Lần nộp cũ chuyển Superseded, không bị xoá (BR-01); lần nộp sau là AttemptNumber + 1.
/// </summary>
public sealed record WithdrawStageTaskCommand(Guid ChapterId, Guid TaskId, Guid UserId) : IRequest<Result<StageTaskDto>>, ITransactionalCommand;

public sealed class WithdrawStageTaskCommandHandler : IRequestHandler<WithdrawStageTaskCommand, Result<StageTaskDto>>
{
    private readonly IPanelForgeDbContext _db;
    public WithdrawStageTaskCommandHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<StageTaskDto>> Handle(WithdrawStageTaskCommand cmd, CancellationToken ct)
    {
        static Result<StageTaskDto> Fail(string msg, string code) => Result<StageTaskDto>.Failure(msg, code);

        var chapter = await _db.Chapters.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cmd.ChapterId, ct);
        if (chapter is null) return Fail($"Chapter {cmd.ChapterId} không tồn tại.", ResultErrorCodes.NotFound);
        if (AssignmentRules.ChapterNotEditableReason(chapter) is { } locked) return Fail(locked, ResultErrorCodes.Conflict);

        await _db.LockChapterAsync(chapter.Id, ct);

        var task = await _db.StageTasks.FirstOrDefaultAsync(t => t.Id == cmd.TaskId && t.ChapterId == chapter.Id, ct);
        if (task is null) return Fail($"Ô {cmd.TaskId} không thuộc chương này.", ResultErrorCodes.NotFound);

        var lot = task.LotId is { } lotId ? await _db.StageTaskLots.AsNoTracking().FirstOrDefaultAsync(l => l.Id == lotId, ct) : null;
        if (lot is null || lot.AssigneeUserId != cmd.UserId)
            return Fail("Chỉ người đang giữ ô mới rút được bài đã nộp.", ResultErrorCodes.Forbidden);
        if (task.Status == Domain.Enums.StageTaskStatus.UnderReview)
            return Fail("Editor đang mở duyệt ô này nên không rút được; nhờ Editor trả bài hoặc nhả khoá duyệt.", ResultErrorCodes.Conflict);

        var pending = await TaskGridRules.PendingSubmissionAsync(_db, task, ct);
        try { task.Withdraw(); }
        catch (InvalidOperationException ex) { return Fail(ex.Message, ResultErrorCodes.Conflict); }
        pending?.Supersede();

        task.UpdatedBy = cmd.UserId.ToString();
        await _db.SaveChangesAsync(ct);
        return Result<StageTaskDto>.Success(StageTaskDto.From(task, lot.AssigneeUserId, isStartable: false));
    }
}
