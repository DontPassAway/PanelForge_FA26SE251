using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.Pages.Models;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Pages.Commands.TogglePageContentReady;

/// <summary>
/// V2.5 & Module C: Writer/Producer đánh dấu trang kịch bản đã sẵn sàng cho Artist tiến hành vẽ Layout/Thumbnail song song.
/// </summary>
public sealed record MarkPageContentReadyCommand(Guid ChapterId, Guid PageId, Guid UserId) : IRequest<Result<PageDto>>;

public sealed class MarkPageContentReadyCommandHandler : IRequestHandler<MarkPageContentReadyCommand, Result<PageDto>>
{
    private readonly IPanelForgeDbContext _db;
    private readonly IWorkspaceAuthorizationService _auth;

    public MarkPageContentReadyCommandHandler(IPanelForgeDbContext db, IWorkspaceAuthorizationService auth)
    {
        _db = db;
        _auth = auth;
    }

    public async Task<Result<PageDto>> Handle(MarkPageContentReadyCommand cmd, CancellationToken ct)
    {
        var chapter = await _db.Chapters.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cmd.ChapterId, ct);
        if (chapter is null)
            return Result<PageDto>.Failure($"Chương {cmd.ChapterId} không tồn tại.", ResultErrorCodes.NotFound);

        var workspaceId = await _db.Series.AsNoTracking().Where(s => s.Id == chapter.SeriesId).Select(s => s.WorkspaceId).FirstAsync(ct);
        var role = await _auth.GetMemberRoleAsync(cmd.UserId, workspaceId, ct);
        if (role != WorkspaceRole.Producer && role != WorkspaceRole.Writer)
            return Result<PageDto>.Failure("Chỉ Writer hoặc Producer mới có quyền đánh dấu Content-Ready cho trang.", ResultErrorCodes.Forbidden);

        var page = await _db.Pages.FirstOrDefaultAsync(p => p.Id == cmd.PageId && p.ChapterId == cmd.ChapterId, ct);
        if (page is null)
            return Result<PageDto>.Failure($"Trang {cmd.PageId} không tồn tại trong chương này.", ResultErrorCodes.NotFound);

        page.MarkContentReady(cmd.UserId);
        await _db.SaveChangesAsync(ct);

        return Result<PageDto>.Success(new PageDto(
            page.Id,
            page.ChapterId,
            page.PageNumber,
            page.LayoutFormat.ToString(),
            page.WidthPx,
            page.HeightPx,
            page.Dpi,
            0,
            page.IsContentReady,
            page.ContentReadyAt,
            page.ContentReadyById
        ));
    }
}

/// <summary>
/// V2.5 & Module C: Writer hủy cờ Content-Ready nếu phát hiện cần điều chỉnh lớn trước khi vẽ.
/// </summary>
public sealed record UnmarkPageContentReadyCommand(Guid ChapterId, Guid PageId, Guid UserId) : IRequest<Result<PageDto>>;

public sealed class UnmarkPageContentReadyCommandHandler : IRequestHandler<UnmarkPageContentReadyCommand, Result<PageDto>>
{
    private readonly IPanelForgeDbContext _db;
    private readonly IWorkspaceAuthorizationService _auth;

    public UnmarkPageContentReadyCommandHandler(IPanelForgeDbContext db, IWorkspaceAuthorizationService auth)
    {
        _db = db;
        _auth = auth;
    }

    public async Task<Result<PageDto>> Handle(UnmarkPageContentReadyCommand cmd, CancellationToken ct)
    {
        var chapter = await _db.Chapters.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cmd.ChapterId, ct);
        if (chapter is null)
            return Result<PageDto>.Failure($"Chương {cmd.ChapterId} không tồn tại.", ResultErrorCodes.NotFound);

        var workspaceId = await _db.Series.AsNoTracking().Where(s => s.Id == chapter.SeriesId).Select(s => s.WorkspaceId).FirstAsync(ct);
        var role = await _auth.GetMemberRoleAsync(cmd.UserId, workspaceId, ct);
        if (role != WorkspaceRole.Producer && role != WorkspaceRole.Writer)
            return Result<PageDto>.Failure("Chỉ Writer hoặc Producer mới có quyền thay đổi cờ Content-Ready.", ResultErrorCodes.Forbidden);

        var page = await _db.Pages.FirstOrDefaultAsync(p => p.Id == cmd.PageId && p.ChapterId == cmd.ChapterId, ct);
        if (page is null)
            return Result<PageDto>.Failure($"Trang {cmd.PageId} không tồn tại trong chương này.", ResultErrorCodes.NotFound);

        page.UnmarkContentReady();
        await _db.SaveChangesAsync(ct);

        return Result<PageDto>.Success(new PageDto(
            page.Id,
            page.ChapterId,
            page.PageNumber,
            page.LayoutFormat.ToString(),
            page.WidthPx,
            page.HeightPx,
            page.Dpi,
            0,
            page.IsContentReady,
            page.ContentReadyAt,
            page.ContentReadyById
        ));
    }
}

