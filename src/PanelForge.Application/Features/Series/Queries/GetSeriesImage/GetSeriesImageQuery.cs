using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.Assets.Common;
using PanelForge.Application.Features.Series.Commands.SetSeriesImage;
using PanelForge.Application.Features.Series.Queries.GetSeriesCover;

namespace PanelForge.Application.Features.Series.Queries.GetSeriesImage;

/// <summary>Thành viên Workspace xem avatar/banner hiện tại của Series (quyền kiểm ở route theo seriesId).</summary>
public sealed record GetSeriesImageQuery(Guid SeriesId, SeriesImageKind Kind) : IRequest<Result<SeriesCoverContent>>;

public sealed class GetSeriesImageQueryHandler : IRequestHandler<GetSeriesImageQuery, Result<SeriesCoverContent>>
{
    private readonly IPanelForgeDbContext _db;
    private readonly IAssetStorage _storage;

    public GetSeriesImageQueryHandler(IPanelForgeDbContext db, IAssetStorage storage)
    {
        _db = db;
        _storage = storage;
    }

    public async Task<Result<SeriesCoverContent>> Handle(GetSeriesImageQuery request, CancellationToken ct)
    {
        // Khoá lấy từ hash trong DB, không từ input người dùng
        var sha = await _db.Series.Where(s => s.Id == request.SeriesId)
            .Select(s => request.Kind == SeriesImageKind.Avatar ? s.AvatarImageSha256 : s.BannerImageSha256)
            .FirstOrDefaultAsync(ct);
        if (sha is null)
            return NotFound();

        await using var stream = await _storage.OpenReadAsync(
            SetSeriesImageCommandHandler.StorageKey(request.SeriesId, request.Kind, sha), ct);
        if (stream is null)
            return NotFound();

        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, ct);
        var content = buffer.ToArray();
        return Result<SeriesCoverContent>.Success(
            new SeriesCoverContent(content, AssetRules.DetectImageContentType(content) ?? "application/octet-stream"));
    }

    private static Result<SeriesCoverContent> NotFound()
        => Result<SeriesCoverContent>.Failure("Series chưa có ảnh này.", ResultErrorCodes.NotFound);
}
