using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.Assets.Models;
using PanelForge.Application.Features.Chapters.Common;
using PanelForge.Domain.Entities.Preview;

namespace PanelForge.Application.Features.PreviewLinks.Queries;

/// <summary>
/// Ảnh trang trong preview: token ảnh ngắn hạn (BR-16) gắn (AssetReference, PreviewLink). Mỗi lần vẫn kiểm link còn hiệu lực
/// và ảnh thuộc snapshot của link; ghi log mọi lần (BR-17).
/// </summary>
public sealed record GetPreviewImageQuery(string? Token, string? IpAddress) : IRequest<Result<AssetDownloadDto>>;

public sealed class GetPreviewImageQueryHandler : IRequestHandler<GetPreviewImageQuery, Result<AssetDownloadDto>>
{
    private readonly IPanelForgeDbContext _db;
    private readonly IAssetLinkSigner _signer;
    private readonly IAssetStorage _storage;
    private readonly IPageRepository _pages;
    private readonly IPageLetteringCompositor _compositor;

    public GetPreviewImageQueryHandler(IPanelForgeDbContext db, IAssetLinkSigner signer, IAssetStorage storage,
        IPageRepository pages, IPageLetteringCompositor compositor)
    {
        _db = db;
        _signer = signer;
        _storage = storage;
        _pages = pages;
        _compositor = compositor;
    }

    public async Task<Result<AssetDownloadDto>> Handle(GetPreviewImageQuery q, CancellationToken ct)
    {
        var claims = string.IsNullOrWhiteSpace(q.Token) ? null : _signer.Validate(q.Token);
        var link = claims is null ? null : await _db.PreviewLinks.AsNoTracking().FirstOrDefaultAsync(l => l.Id == claims.UserId, ct);

        var deny = claims is null || link is null ? "InvalidToken"
            : link.DenyReason(DateTime.UtcNow)
              ?? (link.SnapshotArtworkIds.Contains(claims.AssetReferenceId) ? null : "NotInSnapshot");
        _db.PreviewLinkAccessLogs.Add(PreviewLinkAccessLog.Create(link?.Id, "Image", deny is null, deny, q.IpAddress));
        await _db.SaveChangesAsync(ct);
        if (deny is not null)
            return Result<AssetDownloadDto>.Failure("Không xem được ảnh: link hết hạn, bị thu hồi hoặc không hợp lệ.", ResultErrorCodes.Forbidden);

        // Dựng cả trang từ mọi artwork của trang này trong snapshot (ảnh cả trang, hoặc ghép ảnh từng Panel) + lớp chữ
        var pageId = await _db.AssetReferences.AsNoTracking().Where(r => r.Id == claims!.AssetReferenceId)
            .Select(r => r.PageId).FirstAsync(ct);
        var image = await PageImageBuilder.BuildAsync(_db, _storage, _pages, _compositor, pageId, link!.SnapshotArtworkIds, ct);
        if (image is null)
            return Result<AssetDownloadDto>.Failure("Không tìm thấy ảnh.", ResultErrorCodes.NotFound);

        return Result<AssetDownloadDto>.Success(new AssetDownloadDto(
            new MemoryStream(image.Content), image.ContentType, $"trang-{image.PageNumber}.png"));
    }
}
