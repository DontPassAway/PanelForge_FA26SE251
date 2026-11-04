using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.Assets.Common;
using PanelForge.Application.Features.Chapters.Commands.SetChapterCover;
using PanelForge.Application.Features.Series.Queries.GetSeriesCover;

namespace PanelForge.Application.Features.Chapters.Queries.GetChapterCover;

/// <summary>Thành viên Workspace xem ảnh bìa hiện tại của chương (quyền kiểm ở route theo chapterId).</summary>
public sealed record GetChapterCoverQuery(Guid ChapterId) : IRequest<Result<SeriesCoverContent>>;

public sealed class GetChapterCoverQueryHandler : IRequestHandler<GetChapterCoverQuery, Result<SeriesCoverContent>>
{
    private readonly IPanelForgeDbContext _db;
    private readonly IAssetStorage _storage;

    public GetChapterCoverQueryHandler(IPanelForgeDbContext db, IAssetStorage storage)
    {
        _db = db;
        _storage = storage;
    }

    public async Task<Result<SeriesCoverContent>> Handle(GetChapterCoverQuery request, CancellationToken ct)
    {
        // Khoá lấy từ hash trong DB, không từ input người dùng
        var sha = await _db.Chapters.Where(c => c.Id == request.ChapterId)
            .Select(c => c.CoverImageSha256).FirstOrDefaultAsync(ct);
        if (sha is null)
            return NotFound();

        await using var stream = await _storage.OpenReadAsync(SetChapterCoverCommandHandler.StorageKey(request.ChapterId, sha), ct);
        if (stream is null)
            return NotFound();

        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, ct);
        var content = buffer.ToArray();
        return Result<SeriesCoverContent>.Success(
            new SeriesCoverContent(content, AssetRules.DetectImageContentType(content) ?? "application/octet-stream"));
    }

    private static Result<SeriesCoverContent> NotFound()
        => Result<SeriesCoverContent>.Failure("Chương chưa có ảnh bìa.", ResultErrorCodes.NotFound);
}
