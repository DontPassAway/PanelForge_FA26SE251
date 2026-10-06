using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Assets.Models;

namespace PanelForge.Application.Features.Assets.Queries.GetPageArtwork;

/// <summary>Các phiên bản artwork của một Page (UC-07), theo stage rồi theo phiên bản tăng dần. Lọc tuỳ chọn theo stage.</summary>
public sealed record GetPageArtworkQuery(Guid ChapterId, Guid PageId, Guid? StageId = null)
    : IRequest<Result<IReadOnlyList<AssetReferenceDto>>>;

public sealed class GetPageArtworkQueryHandler
    : IRequestHandler<GetPageArtworkQuery, Result<IReadOnlyList<AssetReferenceDto>>>
{
    private readonly IPanelForgeDbContext _db;

    public GetPageArtworkQueryHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<IReadOnlyList<AssetReferenceDto>>> Handle(GetPageArtworkQuery q, CancellationToken ct)
    {
        if (!await _db.Pages.AsNoTracking().AnyAsync(p => p.Id == q.PageId && p.ChapterId == q.ChapterId, ct))
            return Result<IReadOnlyList<AssetReferenceDto>>.Failure("Page không thuộc Chapter.", ResultErrorCodes.NotFound);

        var query = _db.AssetReferences.AsNoTracking().Where(r => r.PageId == q.PageId);
        if (q.StageId is { } stageId) query = query.Where(r => r.PipelineStageId == stageId);

        var refs = await query.OrderBy(r => r.PipelineStageId).ThenBy(r => r.VersionNumber).ToListAsync(ct);

        var assetIds = refs.Select(r => r.AssetId).Distinct().ToList();
        var assets = (await _db.Assets.AsNoTracking().Where(a => assetIds.Contains(a.Id)).ToListAsync(ct))
            .ToDictionary(a => a.Id);

        var items = refs.Select(r => AssetReferenceDto.From(r, assets[r.AssetId])).ToList();
        return Result<IReadOnlyList<AssetReferenceDto>>.Success(items);
    }
}
