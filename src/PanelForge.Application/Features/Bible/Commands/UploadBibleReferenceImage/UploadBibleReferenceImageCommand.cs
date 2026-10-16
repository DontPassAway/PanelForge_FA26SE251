using MediatR;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.Assets.Common;

namespace PanelForge.Application.Features.Bible.Commands.UploadBibleReferenceImage;

/// <summary>
/// FR-Producer-02: Producer tải ảnh reference sheet lên kho. URL trả về được gửi vào <c>ReferenceImageUrl</c>
/// khi tạo mục hoặc thêm revision, nên ảnh gắn theo từng version (BR-01, BR-15). Khoá theo SHA-256 nội dung:
/// cùng ảnh thì cùng URL, file đã lưu không bao giờ bị ghi đè.
/// </summary>
public sealed record UploadBibleReferenceImageCommand(Guid SeriesId, byte[] Content) : IRequest<Result<BibleReferenceImageDto>>;

public sealed record BibleReferenceImageDto(string Url, string Sha256, string ContentType, long SizeBytes);

public sealed class UploadBibleReferenceImageCommandHandler : IRequestHandler<UploadBibleReferenceImageCommand, Result<BibleReferenceImageDto>>
{
    public const long MaxBytes = 10 * 1024 * 1024;

    private readonly IAssetStorage _storage;

    public UploadBibleReferenceImageCommandHandler(IAssetStorage storage) => _storage = storage;

    public static string StorageKey(Guid seriesId, string sha256) => $"bible/{seriesId:N}/{sha256}";

    public async Task<Result<BibleReferenceImageDto>> Handle(UploadBibleReferenceImageCommand cmd, CancellationToken ct)
    {
        if (cmd.Content.Length == 0 || cmd.Content.Length > MaxBytes)
            return Result<BibleReferenceImageDto>.Failure("Ảnh reference sheet phải từ 1 byte đến 10 MB.", ResultErrorCodes.Validation);

        var contentType = AssetRules.DetectImageContentType(cmd.Content);
        if (contentType is null)
            return Result<BibleReferenceImageDto>.Failure("Chỉ nhận ảnh PNG, JPEG hoặc WebP.", ResultErrorCodes.Validation);

        var sha = AssetRules.ComputeSha256(cmd.Content);
        await _storage.SaveAsync(StorageKey(cmd.SeriesId, sha), cmd.Content,
            new AssetObjectInfo(contentType, new Dictionary<string, string> { ["kind"] = "bible-reference" }), ct);

        return Result<BibleReferenceImageDto>.Success(new BibleReferenceImageDto(
            $"/api/series/{cmd.SeriesId}/bible/assets/{sha}", sha, contentType, cmd.Content.Length));
    }
}
