using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.Assets.Common;
using PanelForge.Application.Features.Series.Commands.SetSeriesCover;

namespace PanelForge.Application.Features.Series.Queries.GetSeriesCover;

/// <summary>Thành viên Workspace xem ảnh bìa hiện tại của Series (quyền kiểm ở route theo seriesId).</summary>
public sealed record GetSeriesCoverQuery(Guid SeriesId) : IRequest<Result<SeriesCoverContent>>;

public sealed record SeriesCoverContent(byte[] Content, string ContentType);

public sealed class GetSeriesCoverQueryHandler : IRequestHandler<GetSeriesCoverQuery, Result<SeriesCoverContent>>
{
    private readonly IPanelForgeDbContext _db;
    private readonly IAssetStorage _storage;

    public GetSeriesCoverQueryHandler(IPanelForgeDbContext db, IAssetStorage storage)
    {
        _db = db;
        _storage = storage;
    }

    public async Task<Result<SeriesCoverContent>> Handle(GetSeriesCoverQuery request, CancellationToken ct)
    {
        // Khoá lấy từ hash trong DB, không từ input người dùng
        var sha = await _db.Series.Where(s => s.Id == request.SeriesId)
            .Select(s => s.CoverImageSha256).FirstOrDefaultAsync(ct);
        if (sha is null)
            return NotFound();

        await using var stream = await _storage.OpenReadAsync(SetSeriesCoverCommandHandler.StorageKey(request.SeriesId, sha), ct);
        if (stream is null)
            return NotFound();

        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, ct);
        var content = buffer.ToArray();
        return Result<SeriesCoverContent>.Success(
            new SeriesCoverContent(content, AssetRules.DetectImageContentType(content) ?? "application/octet-stream"));
    }

    private static Result<SeriesCoverContent> NotFound()
        => Result<SeriesCoverContent>.Failure("Series chưa có ảnh bìa.", ResultErrorCodes.NotFound);
}
