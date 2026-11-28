using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.PageComments.Common;
using PanelForge.Application.Features.PageComments.Models;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.PageComments.Commands.ReopenPageComment;

public sealed record ReopenPageCommentCommand(
    Guid ChapterId,
    Guid CommentId,
    Guid UserId)
    : IRequest<Result<PageCommentDto>>;

public sealed class ReopenPageCommentCommandHandler
    : IRequestHandler<ReopenPageCommentCommand, Result<PageCommentDto>>
{
    private readonly IPanelForgeDbContext _db;

    public ReopenPageCommentCommandHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<PageCommentDto>> Handle(ReopenPageCommentCommand cmd, CancellationToken ct)
    {
        var comment = await _db.PageComments
            .FirstOrDefaultAsync(c => c.Id == cmd.CommentId && c.ChapterId == cmd.ChapterId, ct);
        if (comment is null)
            return Result<PageCommentDto>.Failure("Góp ý không tồn tại.", ResultErrorCodes.NotFound);

        try
        {
            comment.Reopen(cmd.UserId);
        }
        catch (InvalidOperationException ex)
        {
            return Result<PageCommentDto>.Failure(ex.Message, ResultErrorCodes.Conflict);
        }
        catch (ArgumentException ex)
        {
            return Result<PageCommentDto>.Failure(ex.Message, ResultErrorCodes.Validation);
        }

        PageCommentNotifications.NotifyAuthor(_db, comment, cmd.UserId,
            NotificationType.PageCommentReopened, "Góp ý trên trang của bạn được mở lại");
        await _db.SaveChangesAsync(ct);
        return Result<PageCommentDto>.Success(await PageCommentMapper.ToDtoAsync(_db, comment, ct));
    }
}
