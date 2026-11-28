using MediatR;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.Assets.Common;

namespace PanelForge.Application.Features.Bible.Commands.UploadBibleReferenceImage;

/// <summary>
/// FR-Producer-02, hướng B: Producer, Writer hoặc Artist tải ảnh reference sheet lên kho (ảnh chỉ thành canon khi Producer khoá revision chứa nó). URL trả về được gửi vào <c>ReferenceImageUrl</c>
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

    /// <summary>
    /// Q20 / BR-04: SHA-256 của ảnh concept do server lấy, không tin giá trị client gửi. Ảnh tải lên kho Bible của series có URL
    /// khoá theo SHA server tính lúc tải lên (file không bị ghi đè) → lấy SHA từ URL và kiểm file còn trong kho.
    /// URL ngoài kho của series → (true, null): không có SHA. URL đúng dạng kho nhưng không có file → (false, null).
    /// </summary>
    public static async Task<(bool Found, string? Sha256)> ResolveShaAsync(
        IAssetStorage storage, Guid seriesId, string imageUrl, CancellationToken ct)
    {
        var path = imageUrl.Trim().Split('?', '#')[0];
        var prefix = $"/api/series/{seriesId}/bible/assets/";
        var at = path.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
        if (at < 0) return (true, null);

        var sha = path[(at + prefix.Length)..].ToLowerInvariant();
        if (sha.Length != 64 || !sha.All(Uri.IsHexDigit)) return (false, null);
        await using var file = await storage.OpenReadAsync(StorageKey(seriesId, sha), ct);
        return file is null ? (false, null) : (true, sha);
    }

    public const string ConceptImageNotFound = "Ảnh concept không có trong kho Bible của series, hãy tải ảnh lên lại.";

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
