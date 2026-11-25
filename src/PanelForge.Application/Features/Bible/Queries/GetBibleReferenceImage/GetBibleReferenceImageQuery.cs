using System.Text.RegularExpressions;
using MediatR;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.Assets.Common;
using PanelForge.Application.Features.Bible.Commands.UploadBibleReferenceImage;

namespace PanelForge.Application.Features.Bible.Queries.GetBibleReferenceImage;

/// <summary>FR-Artist-04: thành viên Workspace xem ảnh reference sheet của Series (quyền kiểm ở route theo seriesId).</summary>
public sealed record GetBibleReferenceImageQuery(Guid SeriesId, string Sha256) : IRequest<Result<BibleReferenceImageContent>>;

public sealed record BibleReferenceImageContent(byte[] Content, string ContentType);

public sealed partial class GetBibleReferenceImageQueryHandler : IRequestHandler<GetBibleReferenceImageQuery, Result<BibleReferenceImageContent>>
{
    private readonly IAssetStorage _storage;

    public GetBibleReferenceImageQueryHandler(IAssetStorage storage) => _storage = storage;

    public async Task<Result<BibleReferenceImageContent>> Handle(GetBibleReferenceImageQuery request, CancellationToken ct)
    {
        // Chỉ nhận hash hex 64 ký tự: không cho ghép đường dẫn tuỳ ý vào storage key
        if (!Sha256Pattern().IsMatch(request.Sha256))
            return NotFound();

        await using var stream = await _storage.OpenReadAsync(
            UploadBibleReferenceImageCommandHandler.StorageKey(request.SeriesId, request.Sha256.ToLowerInvariant()), ct);
        if (stream is null)
            return NotFound();

        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, ct);
        var content = buffer.ToArray();
        var contentType = AssetRules.DetectImageContentType(content) ?? "application/octet-stream";
        return Result<BibleReferenceImageContent>.Success(new BibleReferenceImageContent(content, contentType));
    }

    private static Result<BibleReferenceImageContent> NotFound()
        => Result<BibleReferenceImageContent>.Failure("Không tìm thấy ảnh reference sheet.", ResultErrorCodes.NotFound);

    [GeneratedRegex("^[0-9a-fA-F]{64}$")]
    private static partial Regex Sha256Pattern();
}
