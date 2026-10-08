using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.Assets.Models;
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

    public GetPreviewImageQueryHandler(IPanelForgeDbContext db, IAssetLinkSigner signer, IAssetStorage storage)
    {
        _db = db;
        _signer = signer;
        _storage = storage;
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

        var reference = await _db.AssetReferences.AsNoTracking().FirstAsync(r => r.Id == claims!.AssetReferenceId, ct);
        var asset = await _db.Assets.AsNoTracking().FirstOrDefaultAsync(a => a.Id == reference.AssetId, ct);
        var stream = asset is null ? null : await _storage.OpenReadAsync(asset.StorageKey, ct);
        if (asset is null || stream is null)
            return Result<AssetDownloadDto>.Failure("Không tìm thấy ảnh.", ResultErrorCodes.NotFound);

        return Result<AssetDownloadDto>.Success(new AssetDownloadDto(stream, asset.ContentType, reference.FileName));
    }
}
