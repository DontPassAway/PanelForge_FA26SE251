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
/// (gỡ thành viên là thu hồi quyền ngay). Mỗi lần tải ghi một dòng nhật ký truy cập. Là command vì có ghi nhật ký.
/// </summary>
public sealed record DownloadAssetCommand(string Token, string? IpAddress) : IRequest<Result<AssetDownloadDto>>;

public sealed class DownloadAssetCommandHandler : IRequestHandler<DownloadAssetCommand, Result<AssetDownloadDto>>
{
    private readonly IPanelForgeDbContext _db;
    private readonly IAssetStorage _storage;
    private readonly IAssetLinkSigner _signer;
    private readonly IWorkspaceAuthorizationService _auth;

    public DownloadAssetCommandHandler(
        IPanelForgeDbContext db, IAssetStorage storage, IAssetLinkSigner signer, IWorkspaceAuthorizationService auth)
    {
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
        var stream = asset is null ? null : await _storage.OpenReadAsync(asset.StorageKey, ct);
        if (asset is null || stream is null)
            return Result<AssetDownloadDto>.Failure("Không tìm thấy nội dung artwork.", ResultErrorCodes.NotFound);

        _db.AssetAccessLogs.Add(AssetAccessLog.Create(reference.Id, claims.UserId, cmd.IpAddress));
        await _db.SaveChangesAsync(ct);

        return Result<AssetDownloadDto>.Success(new AssetDownloadDto(stream, asset.ContentType, reference.FileName));
    }
}
