using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.PreviewLinks.Common;
using PanelForge.Application.Features.PreviewLinks.Models;

namespace PanelForge.Application.Features.PreviewLinks.Commands;

/// <summary>
/// UC-21: người nhận (không tài khoản) mở link. Kiểm token, hạn, thu hồi và ghi log (BR-09, BR-17); trả đúng snapshot,
/// đọc theo hướng đọc của Series. Ảnh qua link ngắn hạn ký riêng cho link này (không dùng quyền Workspace, BR-24).
/// </summary>
public sealed record OpenPreviewCommand(string? Token, string? IpAddress) : IRequest<Result<PreviewChapterDto>>;

public sealed class OpenPreviewCommandHandler : IRequestHandler<OpenPreviewCommand, Result<PreviewChapterDto>>
{
    private readonly IPanelForgeDbContext _db;
    private readonly IAssetLinkSigner _signer;

    public OpenPreviewCommandHandler(IPanelForgeDbContext db, IAssetLinkSigner signer)
    {
        _db = db;
        _signer = signer;
    }

    public async Task<Result<PreviewChapterDto>> Handle(OpenPreviewCommand cmd, CancellationToken ct)
    {
        var (link, deny) = await PreviewLinkRules.ResolveAsync(_db, cmd.Token, "Open", cmd.IpAddress, ct);
        await _db.SaveChangesAsync(ct); // log được lưu cả khi bị từ chối
        if (link is null)
            return Result<PreviewChapterDto>.Failure(PreviewLinkRules.Message(deny), ResultErrorCodes.Forbidden);

        var chapter = await _db.Chapters.AsNoTracking().FirstAsync(c => c.Id == link.ChapterId, ct);
        var series = await _db.Series.AsNoTracking().FirstAsync(s => s.Id == chapter.SeriesId, ct);

        var snapshotIds = link.SnapshotArtworkIds;
        var refs = await _db.AssetReferences.AsNoTracking().Where(r => snapshotIds.Contains(r.Id))
            .Select(r => new { r.Id, r.PageId, r.VersionNumber }).ToListAsync(ct);
        var pageIds = refs.Select(r => r.PageId).ToList();
        var pages = await _db.Pages.AsNoTracking().Where(p => pageIds.Contains(p.Id))
            .Select(p => new { p.Id, p.PageNumber }).ToListAsync(ct);
        var panels = (await _db.Panels.AsNoTracking().Where(p => pageIds.Contains(p.PageId))
                .Select(p => new { p.Id, p.PageId, p.PanelNumber, p.BoundingBox }).ToListAsync(ct))
            .ToLookup(p => p.PageId);

        var items = (
            from r in refs
            join p in pages on r.PageId equals p.Id
            orderby p.PageNumber
            select new PreviewPageDto(
                p.Id, p.PageNumber, r.Id, r.VersionNumber,
                $"/api/preview/images?token={Uri.EscapeDataString(_signer.Create(r.Id, link.Id, PreviewLinkRules.ImageLinkLifetime))}",
                panels[p.Id].OrderBy(x => x.PanelNumber).Select(x => new PreviewPanelDto(x.Id, x.PanelNumber, x.BoundingBox)).ToList()))
            .ToList();

        return Result<PreviewChapterDto>.Success(new PreviewChapterDto(
            chapter.Id, series.Title, chapter.ChapterNumber, chapter.Title, series.ReadingDirection,
            link.RecipientEmail, $"Bản xem thử – {link.RecipientEmail} – không chia sẻ", link.ExpiresAt, link.ChapterVersion, items));
    }
}
