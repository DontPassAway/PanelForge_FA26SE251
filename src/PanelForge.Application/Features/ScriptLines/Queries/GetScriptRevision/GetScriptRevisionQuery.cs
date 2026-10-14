using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.ScriptLines.Common;
using PanelForge.Application.Features.ScriptLines.Models;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.ScriptLines.Queries.GetScriptRevision;

/// <summary>
/// UC-18: nội dung (các dòng) của một revision Script, để xem lại và so sánh trước khi khôi phục.
/// Quyền thành viên Workspace kiểm ở controller qua {chapterId}.
/// </summary>
public sealed record GetScriptRevisionQuery(Guid ChapterId, Guid SceneId, Guid RevisionId)
    : IRequest<Result<ScriptRevisionDetailDto>>;

public sealed class GetScriptRevisionQueryHandler : IRequestHandler<GetScriptRevisionQuery, Result<ScriptRevisionDetailDto>>
{
    private readonly IPanelForgeDbContext _db;

    public GetScriptRevisionQueryHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<ScriptRevisionDetailDto>> Handle(GetScriptRevisionQuery q, CancellationToken ct)
    {
        // Chặn IDOR: revision phải thuộc Scene, Scene phải thuộc Chapter trên route
        var revision = await _db.ContentRevisions.AsNoTracking().FirstOrDefaultAsync(
            r => r.Id == q.RevisionId && r.SceneId == q.SceneId && r.Kind == ContentRevisionKind.Script &&
                 _db.Scenes.Any(s => s.Id == r.SceneId && s.ChapterId == q.ChapterId), ct);
        if (revision is null)
            return Result<ScriptRevisionDetailDto>.Failure(
                $"Revision script {q.RevisionId} không tồn tại trong Scene này.", ResultErrorCodes.NotFound);

        var lines = ScriptLineRules.ParseRevisionLines(revision.PayloadJson);
        if (lines is null)
            return Result<ScriptRevisionDetailDto>.Failure("Nội dung revision không đọc được.", ResultErrorCodes.Validation);

        var latestVersion = await _db.ContentRevisions.AsNoTracking()
            .Where(r => r.SceneId == q.SceneId && r.Kind == ContentRevisionKind.Script)
            .MaxAsync(r => r.VersionNumber, ct);

        return Result<ScriptRevisionDetailDto>.Success(new ScriptRevisionDetailDto(
            revision.Id, revision.SceneId, revision.VersionNumber, revision.Summary, revision.PreviousRevisionId,
            revision.SourceSuggestionId, revision.VersionNumber == latestVersion, revision.CreatedAt, revision.CreatedBy,
            lines.Select(l => new ScriptRevisionLineDto(l.LineId, l.Order, l.SpeakerId, l.Dialogue, l.StageDirection)).ToList()));
    }
}
