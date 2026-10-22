using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.Assets.Common;

namespace PanelForge.Application.Features.Workspaces.Commands.SetWorkspaceCover;

/// <summary>
/// Producer / Owner đặt ảnh bìa Studio (PNG/JPEG/WebP, ≤ 5 MB) hoặc xoá bìa (Content = null).
/// Lưu qua IAssetStorage (R2) theo SHA-256 nội dung.
/// </summary>
public sealed record SetWorkspaceCoverCommand(Guid WorkspaceId, Guid UserId, byte[]? Content) : IRequest<Result<string?>>;

public sealed class SetWorkspaceCoverCommandHandler : IRequestHandler<SetWorkspaceCoverCommand, Result<string?>>
{
    public const long MaxBytes = 5 * 1024 * 1024;

    private readonly IPanelForgeDbContext _db;
    private readonly IAssetStorage _storage;

    public SetWorkspaceCoverCommandHandler(IPanelForgeDbContext db, IAssetStorage storage)
    {
        _db = db;
        _storage = storage;
    }

    public static string StorageKey(Guid workspaceId, string sha256) => $"covers/workspaces/{workspaceId:N}/{sha256}";

    /// <summary>URL ảnh bìa cho DTO; v đổi theo nội dung để trình duyệt không dính cache bìa cũ.</summary>
    public static string? CoverUrl(Guid workspaceId, string? sha256)
        => sha256 is null ? null : $"/api/workspaces/{workspaceId}/cover?v={sha256[..12]}";

    public async Task<Result<string?>> Handle(SetWorkspaceCoverCommand cmd, CancellationToken ct)
    {
        var workspace = await _db.StudioWorkspaces.FirstOrDefaultAsync(w => w.Id == cmd.WorkspaceId && !w.IsDeleted, ct);
        if (workspace is null)
            return Result<string?>.Failure($"Workspace {cmd.WorkspaceId} không tồn tại.", ResultErrorCodes.NotFound);

        string? sha = null;
        if (cmd.Content is not null)
        {
            if (cmd.Content.Length == 0 || cmd.Content.Length > MaxBytes)
                return Result<string?>.Failure("Ảnh bìa phải từ 1 byte đến 5 MB.", ResultErrorCodes.Validation);

            var contentType = AssetRules.DetectImageContentType(cmd.Content);
            if (contentType is null)
                return Result<string?>.Failure("Chỉ nhận ảnh PNG, JPEG hoặc WebP.", ResultErrorCodes.Validation);

            sha = AssetRules.ComputeSha256(cmd.Content);
            await _storage.SaveAsync(StorageKey(workspace.Id, sha), cmd.Content,
                new AssetObjectInfo(contentType, new Dictionary<string, string> { ["kind"] = "workspace-cover" }), ct);
        }

        workspace.SetCover(sha);
        workspace.UpdatedBy = cmd.UserId.ToString();
        await _db.SaveChangesAsync(ct);
        return Result<string?>.Success(CoverUrl(workspace.Id, sha));
    }
}
