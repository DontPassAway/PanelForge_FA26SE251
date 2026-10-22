using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.Assets.Models;
using PanelForge.Domain.Entities.Assets;

namespace PanelForge.Application.Features.Assets.Commands.DownloadAsset;

/// <summary>
/// BR-16: tải artwork qua link ngắn hạn. Token phải đúng chữ ký và còn hạn; người nhận link phải CÒN là thành viên Workspace
/// (gỡ thành viên là thu hồi quyền ngay). Ghi nhật ký truy cập, gộp các lần tải cùng người + cùng artwork trong
/// <see cref="AccessLogWindow"/> thành một dòng. <paramref name="Thumbnail"/> = true trả bản WebP thu nhỏ (tạo lần đầu rồi lưu kho).
/// Là command vì có ghi nhật ký.
/// </summary>
public sealed record DownloadAssetCommand(string Token, string? IpAddress, bool Thumbnail = false) : IRequest<Result<AssetDownloadDto>>;

public sealed class DownloadAssetCommandHandler : IRequestHandler<DownloadAssetCommand, Result<AssetDownloadDto>>
{
    private readonly IPanelForgeDbContext _db;
    private readonly IAssetStorage _storage;
    private readonly IAssetLinkSigner _signer;
    private readonly IWorkspaceAuthorizationService _auth;
    private readonly IImageThumbnailer _thumbnailer;

    public static readonly TimeSpan AccessLogWindow = TimeSpan.FromMinutes(5);
    public const int ThumbnailMaxWidth = 400;

    public DownloadAssetCommandHandler(
        IPanelForgeDbContext db, IAssetStorage storage, IAssetLinkSigner signer, IWorkspaceAuthorizationService auth,
        IImageThumbnailer thumbnailer)
    {
        _thumbnailer = thumbnailer;
        _db = db;
        _storage = storage;
        _signer = signer;
        _auth = auth;
    }

    public async Task<Result<AssetDownloadDto>> Handle(DownloadAssetCommand cmd, CancellationToken ct)
    {
        var claims = _signer.Validate(cmd.Token);
        if (claims is null)
            return Result<AssetDownloadDto>.Failure("Link không hợp lệ hoặc đã hết hạn.", ResultErrorCodes.Unauthorized);

        var reference = await _db.AssetReferences.AsNoTracking().FirstOrDefaultAsync(r => r.Id == claims.AssetReferenceId, ct);
        if (reference is null)
            return Result<AssetDownloadDto>.Failure("Artwork không tồn tại.", ResultErrorCodes.NotFound);

        if (!await _auth.IsMemberAsync(claims.UserId, reference.WorkspaceId, ct))
            return Result<AssetDownloadDto>.Failure("Người nhận link không còn là thành viên của Workspace.", ResultErrorCodes.Forbidden);

        var asset = await _db.Assets.AsNoTracking().FirstOrDefaultAsync(a => a.Id == reference.AssetId, ct);
        if (asset is null)
            return Result<AssetDownloadDto>.Failure("Không tìm thấy nội dung artwork.", ResultErrorCodes.NotFound);

        var download = cmd.Thumbnail ? await OpenThumbnailAsync(asset, reference.FileName, ct) : null;
        if (download is null)
        {
            var stream = await _storage.OpenReadAsync(asset.StorageKey, ct);
            if (stream is null)
                return Result<AssetDownloadDto>.Failure("Không tìm thấy nội dung artwork.", ResultErrorCodes.NotFound);
            download = new AssetDownloadDto(stream, asset.ContentType, reference.FileName);
        }

        var since = DateTime.UtcNow - AccessLogWindow;
        var loggedRecently = await _db.AssetAccessLogs.AnyAsync(
            l => l.AssetReferenceId == reference.Id && l.UserId == claims.UserId && l.AccessedAt >= since, ct);
        if (!loggedRecently)
        {
            _db.AssetAccessLogs.Add(AssetAccessLog.Create(reference.Id, claims.UserId, cmd.IpAddress));
            await _db.SaveChangesAsync(ct);
        }

        return Result<AssetDownloadDto>.Success(download);
    }

    /// <summary>Bản thu nhỏ theo khoá nội dung gốc; chưa có thì tạo và lưu. Null nếu file không phải ảnh giải mã được.</summary>
    private async Task<AssetDownloadDto?> OpenThumbnailAsync(Asset asset, string fileName, CancellationToken ct)
    {
        var key = $"thumbs/{asset.StorageKey}.w{ThumbnailMaxWidth}.webp";
        var thumbName = Path.ChangeExtension(fileName, ".webp");

        var cached = await _storage.OpenReadAsync(key, ct);
        if (cached is not null) return new AssetDownloadDto(cached, "image/webp", thumbName);

        await using var original = await _storage.OpenReadAsync(asset.StorageKey, ct);
        if (original is null) return null;
        var bytes = _thumbnailer.CreateWebp(original, ThumbnailMaxWidth);
        if (bytes is null) return null;

        await _storage.SaveAsync(key, bytes, new AssetObjectInfo("image/webp", new Dictionary<string, string>()), ct);
        return new AssetDownloadDto(new MemoryStream(bytes, writable: false), "image/webp", thumbName);
    }
}
