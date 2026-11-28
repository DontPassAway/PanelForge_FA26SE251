using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.PageComments.Common;
using PanelForge.Application.Features.PageComments.Models;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.PageComments.Commands.ResolvePageComment;

public sealed record ResolvePageCommentCommand(
    Guid ChapterId,
    Guid CommentId,
    Guid UserId,
    string? Note = null)
    : IRequest<Result<PageCommentDto>>;

public sealed class ResolvePageCommentCommandHandler
    : IRequestHandler<ResolvePageCommentCommand, Result<PageCommentDto>>
{
    private readonly IPanelForgeDbContext _db;

    public ResolvePageCommentCommandHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<PageCommentDto>> Handle(ResolvePageCommentCommand cmd, CancellationToken ct)
    {
        var comment = await _db.PageComments
            .FirstOrDefaultAsync(c => c.Id == cmd.CommentId && c.ChapterId == cmd.ChapterId, ct);
        if (comment is null)
            return Result<PageCommentDto>.Failure("Góp ý không tồn tại.", ResultErrorCodes.NotFound);

        try
        {
            comment.Resolve(cmd.UserId, cmd.Note);
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
            NotificationType.PageCommentResolved, "Góp ý trên trang của bạn đã được xử lý");
        await _db.SaveChangesAsync(ct);
        return Result<PageCommentDto>.Success(await PageCommentMapper.ToDtoAsync(_db, comment, ct));
    }
}
