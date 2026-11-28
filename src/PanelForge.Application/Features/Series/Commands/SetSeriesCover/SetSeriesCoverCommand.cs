using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.Assets.Common;
using PanelForge.Application.Features.Quotas.Common;

namespace PanelForge.Application.Features.Series.Commands.SetSeriesCover;

/// <summary>
/// Producer đặt ảnh bìa Series (PNG/JPEG/WebP, tối đa theo giới hạn upload của Admin) hoặc xoá bìa (<see cref="Content"/> = null).
/// Ảnh lưu theo SHA-256 nội dung nên không bao giờ ghi đè file cũ; Series chỉ giữ hash của bìa hiện tại.
/// </summary>
public sealed record SetSeriesCoverCommand(Guid SeriesId, Guid UserId, byte[]? Content) : IRequest<Result<string?>>;

public sealed class SetSeriesCoverCommandHandler : IRequestHandler<SetSeriesCoverCommand, Result<string?>>
{
    private readonly IPanelForgeDbContext _db;
    private readonly IAssetStorage _storage;

    public SetSeriesCoverCommandHandler(IPanelForgeDbContext db, IAssetStorage storage)
    {
        _db = db;
        _storage = storage;
    }

    public static string StorageKey(Guid seriesId, string sha256) => $"covers/{seriesId:N}/{sha256}";

    /// <summary>URL ảnh bìa cho DTO; <c>v</c> đổi theo nội dung để trình duyệt không giữ bìa cũ.</summary>
    public static string? CoverUrl(Guid seriesId, string? sha256)
        => sha256 is null ? null : $"/api/series/{seriesId}/cover?v={sha256[..12]}";

    public async Task<Result<string?>> Handle(SetSeriesCoverCommand cmd, CancellationToken ct)
    {
        var series = await _db.Series.FirstOrDefaultAsync(s => s.Id == cmd.SeriesId, ct);
        if (series is null)
            return Result<string?>.Failure($"Series {cmd.SeriesId} không tồn tại.", ResultErrorCodes.NotFound);

        string? sha = null;
        if (cmd.Content is not null)
        {
            if (cmd.Content.Length == 0)
                return Result<string?>.Failure("File ảnh rỗng.", ResultErrorCodes.Validation);
            if (await UploadLimits.CheckAsync(_db, UploadKind.CoverImage, cmd.Content.Length, ct) is { } tooLarge)
                return Result<string?>.Failure(tooLarge, ResultErrorCodes.FileTooLarge);

            var contentType = AssetRules.DetectImageContentType(cmd.Content);
            if (contentType is null)
                return Result<string?>.Failure("Chỉ nhận ảnh PNG, JPEG hoặc WebP.", ResultErrorCodes.Validation);

            sha = AssetRules.ComputeSha256(cmd.Content);
            if (!await StorageQuota.TryReserveAsync(_db, _storage, series.WorkspaceId, StorageKey(series.Id, sha), cmd.Content.Length, ct))
                return Result<string?>.Failure(StorageQuota.ExceededMessage, ResultErrorCodes.Conflict);
            await _storage.SaveAsync(StorageKey(series.Id, sha), cmd.Content,
                new AssetObjectInfo(contentType, new Dictionary<string, string> { ["kind"] = "series-cover" }), ct);
        }

        series.SetCover(sha);
        series.UpdatedBy = cmd.UserId.ToString();
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            return Result<string?>.Failure(StorageQuota.ConcurrentMessage, ResultErrorCodes.Conflict);
        }
        return Result<string?>.Success(CoverUrl(series.Id, sha));
    }
}
