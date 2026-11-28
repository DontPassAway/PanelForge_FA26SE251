using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.PageComments.Common;
using PanelForge.Application.Features.PageComments.Models;
using PanelForge.Application.Features.TaskGrid.Commands.ReopenStageTask;
using PanelForge.Application.Features.TaskGrid.Commands.RequestStageTaskChanges;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.PageComments.Commands.ConvertPageCommentToRework;

/// <summary>
/// Producer chuyển một góp ý đang mở thành yêu cầu sửa cho một ô lưới V3 thuộc chương (chương phải đang InProduction).
/// Tái sử dụng đúng luật của ReopenStageTask (ô Approved) và RequestStageTaskChanges (ô Submitted/UnderReview).
/// </summary>
public sealed record ConvertPageCommentToReworkCommand(
    Guid ChapterId,
    Guid CommentId,
    Guid UserId,
    Guid StageTaskId,
    string Reason)
    : IRequest<Result<PageCommentDto>>, ITransactionalCommand;

public sealed class ConvertPageCommentToReworkCommandHandler
    : IRequestHandler<ConvertPageCommentToReworkCommand, Result<PageCommentDto>>
{
    private readonly IPanelForgeDbContext _db;
    private readonly IPageRepository _pages;

    public ConvertPageCommentToReworkCommandHandler(IPanelForgeDbContext db, IPageRepository pages)
    {
        _db = db;
        _pages = pages;
    }

    public async Task<Result<PageCommentDto>> Handle(ConvertPageCommentToReworkCommand cmd, CancellationToken ct)
    {
        static Result<PageCommentDto> Fail(string msg, string code) => Result<PageCommentDto>.Failure(msg, code);

        if (string.IsNullOrWhiteSpace(cmd.Reason))
            return Fail("Yêu cầu sửa bắt buộc phải kèm lý do (BR-06).", ResultErrorCodes.Validation);

        var comment = await _db.PageComments
            .FirstOrDefaultAsync(c => c.Id == cmd.CommentId && c.ChapterId == cmd.ChapterId, ct);
        if (comment is null)
            return Fail("Góp ý không tồn tại.", ResultErrorCodes.NotFound);

        if (comment.Status != PageCommentStatus.Open || comment.StageTaskId.HasValue)
            return Fail("Góp ý này đã được xử lý hoặc đã chuyển thành yêu cầu sửa.", ResultErrorCodes.Conflict);

        var chapter = await _db.Chapters.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cmd.ChapterId, ct);
        if (chapter is null)
            return Fail($"Chapter {cmd.ChapterId} không tồn tại.", ResultErrorCodes.NotFound);

        if (chapter.Status != ChapterStatus.InProduction)
            return Fail($"Chương đang ở trạng thái {chapter.Status}: chương phải được mở lại trước.", ResultErrorCodes.Conflict);

        var task = await _db.StageTasks
            .FirstOrDefaultAsync(t => t.Id == cmd.StageTaskId && t.ChapterId == cmd.ChapterId, ct);
        if (task is null)
            return Fail("Ô công việc không thuộc chương này.", ResultErrorCodes.NotFound);

        if (comment.PageId.HasValue && task.PageId.HasValue && task.PageId != comment.PageId)
            return Fail("Ô công việc không thuộc trang của góp ý này.", ResultErrorCodes.Validation);

        var trimmedReason = cmd.Reason.Trim();

        switch (task.Status)
        {
            case StageTaskStatus.Approved:
            {
                var reopened = await new ReopenStageTaskCommandHandler(_db)
                    .Handle(new ReopenStageTaskCommand(cmd.ChapterId, task.Id, cmd.UserId, trimmedReason), ct);
                if (!reopened.IsSuccess)
                    return Fail(reopened.ErrorMessage!, reopened.ErrorCode ?? ResultErrorCodes.Conflict);
                break;
            }
            case StageTaskStatus.Submitted or StageTaskStatus.UnderReview:
            {
                var requested = await new RequestStageTaskChangesCommandHandler(_db, _pages)
                    .Handle(new RequestStageTaskChangesCommand(cmd.ChapterId, task.Id, cmd.UserId, trimmedReason), ct);
                if (!requested.IsSuccess)
                    return Fail(requested.ErrorMessage!, requested.ErrorCode ?? ResultErrorCodes.Conflict);
                break;
            }
            default:
                return Fail(
                    $"Chỉ có thể chuyển yêu cầu sửa cho ô ở trạng thái Approved, Submitted hoặc UnderReview (hiện tại: {task.Status}).",
                    ResultErrorCodes.Conflict);
        }

        comment.MarkConvertedToRework(task.Id, cmd.UserId);
        await _db.SaveChangesAsync(ct);

        return Result<PageCommentDto>.Success(await PageCommentMapper.ToDtoAsync(_db, comment, ct));
    }
}
