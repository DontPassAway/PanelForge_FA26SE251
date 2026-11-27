using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.PageComments.Common;
using PanelForge.Application.Features.PageComments.Models;
using PanelForge.Domain.Entities.Preview;

namespace PanelForge.Application.Features.PageComments.Commands.CreatePageComment;

public sealed record CreatePageCommentCommand(
    Guid ChapterId,
    Guid AuthorUserId,
    Guid? PageId,
    Guid? PanelId,
    Guid? AssetReferenceId,
    double? PinX,
    double? PinY,
    string Message)
    : IRequest<Result<PageCommentDto>>;

public sealed class CreatePageCommentCommandHandler
    : IRequestHandler<CreatePageCommentCommand, Result<PageCommentDto>>
{
    private readonly IPanelForgeDbContext _db;

    public CreatePageCommentCommandHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<PageCommentDto>> Handle(CreatePageCommentCommand cmd, CancellationToken ct)
    {
        static Result<PageCommentDto> Fail(string msg, string code) => Result<PageCommentDto>.Failure(msg, code);

        var chapter = await _db.Chapters.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cmd.ChapterId, ct);
        if (chapter is null)
            return Fail($"Chapter {cmd.ChapterId} không tồn tại.", ResultErrorCodes.NotFound);

        var resolvedPageId = cmd.PageId;
        if (resolvedPageId.HasValue)
        {
            var pageExists = await _db.Pages.AsNoTracking()
                .AnyAsync(p => p.Id == resolvedPageId.Value && p.ChapterId == chapter.Id, ct);
            if (!pageExists)
                return Fail("Trang không thuộc chương này.", ResultErrorCodes.NotFound);
        }

        if (cmd.PanelId.HasValue)
        {
            var panel = await _db.Panels.AsNoTracking()
                .Where(p => p.Id == cmd.PanelId.Value)
                .Select(p => new { p.Id, p.PageId })
                .FirstOrDefaultAsync(ct);

            if (panel is null)
                return Fail("Khung tranh không tồn tại.", ResultErrorCodes.NotFound);

            if (resolvedPageId.HasValue && panel.PageId != resolvedPageId.Value)
                return Fail("Khung tranh không thuộc trang đã chọn.", ResultErrorCodes.NotFound);

            if (!resolvedPageId.HasValue)
            {
                var panelPageBelongsToChapter = await _db.Pages.AsNoTracking()
                    .AnyAsync(p => p.Id == panel.PageId && p.ChapterId == chapter.Id, ct);
                if (!panelPageBelongsToChapter)
                    return Fail("Khung tranh không thuộc chương này.", ResultErrorCodes.NotFound);

                resolvedPageId = panel.PageId;
            }
        }

        PageComment comment;
        try
        {
            comment = PageComment.Create(
                chapter.SeriesId,
                chapter.Id,
                cmd.AuthorUserId,
                cmd.Message,
                resolvedPageId,
                cmd.PanelId,
                cmd.AssetReferenceId,
                cmd.PinX,
                cmd.PinY);
        }
        catch (ArgumentException ex)
        {
            return Fail(ex.Message, ResultErrorCodes.Validation);
        }

        _db.PageComments.Add(comment);
        await _db.SaveChangesAsync(ct);

        return Result<PageCommentDto>.Success(await PageCommentMapper.ToDtoAsync(_db, comment, ct));
    }
}
