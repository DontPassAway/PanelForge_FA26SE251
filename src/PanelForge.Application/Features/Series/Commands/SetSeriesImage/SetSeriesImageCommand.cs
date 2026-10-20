using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.Assets.Common;

namespace PanelForge.Application.Features.Series.Commands.SetSeriesImage;

/// <summary>Loại ảnh nhận diện Series ngoài bìa dọc: avatar vuông và banner ngang. Mỗi loại một cột, một tiền tố khoá.</summary>
public enum SeriesImageKind
{
    Avatar,
    Banner,
}

/// <summary>
/// Producer đặt avatar/banner Series (PNG/JPEG/WebP, ≤ 5 MB) hoặc xoá (<see cref="Content"/> = null).
/// Cùng cơ chế với bìa dọc: lưu lên <see cref="IAssetStorage"/> (R2 khi cấu hình) theo SHA-256 nội dung, khoá bất biến,
/// không ghi đè file cũ; chỉ cập nhật DB sau khi lưu file thành công. Ảnh mẫu: controller đọc file từ danh mục
/// có kiểm soát rồi gửi bytes qua đúng lệnh này, nên đi qua cùng validation và cùng kho.
/// </summary>
public sealed record SetSeriesImageCommand(Guid SeriesId, Guid UserId, SeriesImageKind Kind, byte[]? Content)
    : IRequest<Result<string?>>;

public sealed class SetSeriesImageCommandHandler : IRequestHandler<SetSeriesImageCommand, Result<string?>>
{
    public const long MaxBytes = 5 * 1024 * 1024;

    private readonly IPanelForgeDbContext _db;
    private readonly IAssetStorage _storage;

    public SetSeriesImageCommandHandler(IPanelForgeDbContext db, IAssetStorage storage)
    {
        _db = db;
        _storage = storage;
    }

    private static string Segment(SeriesImageKind kind) => kind == SeriesImageKind.Avatar ? "avatar" : "banner";

    public static string StorageKey(Guid seriesId, SeriesImageKind kind, string sha256)
        => $"series-{Segment(kind)}s/{seriesId:N}/{sha256}";

    /// <summary>URL ảnh cho DTO; <c>v</c> đổi theo nội dung để không dính cache ảnh cũ.</summary>
    public static string? ImageUrl(Guid seriesId, SeriesImageKind kind, string? sha256)
        => sha256 is null ? null : $"/api/series/{seriesId}/{Segment(kind)}?v={sha256[..12]}";

    public async Task<Result<string?>> Handle(SetSeriesImageCommand cmd, CancellationToken ct)
    {
        var series = await _db.Series.FirstOrDefaultAsync(s => s.Id == cmd.SeriesId, ct);
        if (series is null)
            return Result<string?>.Failure($"Series {cmd.SeriesId} không tồn tại.", ResultErrorCodes.NotFound);

        string? sha = null;
        if (cmd.Content is not null)
        {
            if (cmd.Content.Length == 0 || cmd.Content.Length > MaxBytes)
                return Result<string?>.Failure("Ảnh phải từ 1 byte đến 5 MB.", ResultErrorCodes.Validation);

            var contentType = AssetRules.DetectImageContentType(cmd.Content);
            if (contentType is null)
                return Result<string?>.Failure("Chỉ nhận ảnh PNG, JPEG hoặc WebP.", ResultErrorCodes.Validation);

            sha = AssetRules.ComputeSha256(cmd.Content);
            await _storage.SaveAsync(StorageKey(series.Id, cmd.Kind, sha), cmd.Content,
                new AssetObjectInfo(contentType, new Dictionary<string, string> { ["kind"] = $"series-{Segment(cmd.Kind)}" }), ct);
        }

        if (cmd.Kind == SeriesImageKind.Avatar)
            series.SetAvatar(sha);
        else
            series.SetBanner(sha);
        series.UpdatedBy = cmd.UserId.ToString();
        await _db.SaveChangesAsync(ct);
        return Result<string?>.Success(ImageUrl(series.Id, cmd.Kind, sha));
    }
}
