using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.AiAgents.Common;
using PanelForge.Application.Features.AiAgents.Models;

namespace PanelForge.Application.Features.AiAgents.Queries.GetContentRevisions;

/// <summary>
/// Lịch sử revision script/layout của một Scene (append-only) kèm đề xuất AI nguồn. Client lấy `id` script mới nhất
/// làm baseScriptRevisionId khi chạy workflow lần sau; `mapping` trả lời "panel này sinh từ đề xuất/script nào".
/// </summary>
public sealed record GetContentRevisionsQuery(Guid SeriesId, Guid SceneId, Guid UserId)
    : IRequest<Result<IReadOnlyList<ContentRevisionDto>>>;

public sealed class GetContentRevisionsQueryHandler
    : IRequestHandler<GetContentRevisionsQuery, Result<IReadOnlyList<ContentRevisionDto>>>
{
    private readonly IPanelForgeDbContext _db;
    private readonly IWorkspaceAuthorizationService _auth;

    public GetContentRevisionsQueryHandler(IPanelForgeDbContext db, IWorkspaceAuthorizationService auth)
    {
        _db = db;
        _auth = auth;
    }

    public async Task<Result<IReadOnlyList<ContentRevisionDto>>> Handle(GetContentRevisionsQuery q, CancellationToken ct)
    {
        var workspaceId = await AiRunHelpers.WorkspaceOfSeriesAsync(_db, q.SeriesId, ct);
        if (workspaceId is null)
            return AiRunHelpers.Fail<IReadOnlyList<ContentRevisionDto>>("Series không tồn tại.", ResultErrorCodes.NotFound);
        if (!await _auth.IsMemberAsync(q.UserId, workspaceId.Value, ct))
            return AiRunHelpers.Fail<IReadOnlyList<ContentRevisionDto>>("Bạn không phải thành viên của Workspace này.", ResultErrorCodes.Forbidden);

        // BR-03: Scene phải thuộc Series trên route (qua Chapter)
        var inSeries = await _db.Scenes.AsNoTracking().AnyAsync(
            s => s.Id == q.SceneId && _db.Chapters.Any(c => c.Id == s.ChapterId && c.SeriesId == q.SeriesId), ct);
        if (!inSeries)
            return AiRunHelpers.Fail<IReadOnlyList<ContentRevisionDto>>("Scene không thuộc Series.", ResultErrorCodes.NotFound);

        var list = await _db.ContentRevisions.AsNoTracking()
            .Where(r => r.SceneId == q.SceneId)
            .OrderBy(r => r.Kind).ThenBy(r => r.VersionNumber)
            .ToListAsync(ct);

        return Result<IReadOnlyList<ContentRevisionDto>>.Success(list.Select(r => new ContentRevisionDto(
            r.Id, r.Kind, r.VersionNumber, r.Summary, r.ContentHash, r.PreviousRevisionId, r.SourceSuggestionId,
            AiRunHelpers.ToElement(r.MappingJson), r.CreatedAt)).ToList());
    }
}