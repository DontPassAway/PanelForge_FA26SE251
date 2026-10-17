using System.Security.Cryptography;
using System.Text;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.AiAgents.Common;
using PanelForge.Application.Features.AiAgents.Models;
using PanelForge.Domain.Entities.Content;

namespace PanelForge.Application.Features.AiAgents.Commands.RestoreRevision;

/// <summary>
/// Khôi phục nội dung Scene về một revision cũ: tạo revision MỚI với payload
/// của revision được chọn (append-only — không xoá revision hiện tại, giữ nguyên lịch sử).
/// PreviousRevisionId trỏ về revision nguồn để truy ngược; SourceSuggestionId = null vì đây là hành động người.
/// </summary>
public sealed record RestoreRevisionCommand(
    Guid SeriesId,
    Guid SceneId,
    Guid RevisionId,    // Revision muốn khôi phục
    Guid UserId,
    string? Reason      // Lý do khôi phục (ghi vào Summary)
) : IRequest<Result<ContentRevisionDto>>;

public sealed class RestoreRevisionCommandHandler
    : IRequestHandler<RestoreRevisionCommand, Result<ContentRevisionDto>>
{
    private readonly IPanelForgeDbContext _db;
    private readonly IWorkspaceAuthorizationService _auth;

    public RestoreRevisionCommandHandler(IPanelForgeDbContext db, IWorkspaceAuthorizationService auth)
    {
        _db = db;
        _auth = auth;
    }

    public async Task<Result<ContentRevisionDto>> Handle(RestoreRevisionCommand cmd, CancellationToken ct)
    {
        // ── 1. Kiểm tra quyền ──
        var workspaceId = await AiRunHelpers.WorkspaceOfSeriesAsync(_db, cmd.SeriesId, ct);
        if (workspaceId is null)
            return AiRunHelpers.Fail<ContentRevisionDto>("Series không tồn tại.", ResultErrorCodes.NotFound);
        if (!await _auth.IsMemberAsync(cmd.UserId, workspaceId.Value, ct))
            return AiRunHelpers.Fail<ContentRevisionDto>("Bạn không phải thành viên.", ResultErrorCodes.Forbidden);

        // ── 2. Scene phải thuộc Series trên route (qua Chapter) ──
        var inSeries = await _db.Scenes.AsNoTracking().AnyAsync(
            s => s.Id == cmd.SceneId && _db.Chapters.Any(c => c.Id == s.ChapterId && c.SeriesId == cmd.SeriesId), ct);
        if (!inSeries)
            return AiRunHelpers.Fail<ContentRevisionDto>("Scene không thuộc Series.", ResultErrorCodes.NotFound);

        // ── 3. Tìm revision cần khôi phục ──
        var source = await _db.ContentRevisions.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == cmd.RevisionId && r.SceneId == cmd.SceneId, ct);
        if (source is null)
            return AiRunHelpers.Fail<ContentRevisionDto>("Revision không tồn tại hoặc không thuộc Scene.", ResultErrorCodes.NotFound);

        // ── 4. Tìm version cao nhất hiện tại (cùng Kind) ──
        var currentMax = await _db.ContentRevisions.AsNoTracking()
            .Where(r => r.SceneId == cmd.SceneId && r.Kind == source.Kind)
            .MaxAsync(r => (int?)r.VersionNumber, ct) ?? 0;

        // Nếu source đã là revision mới nhất → không cần restore
        if (source.VersionNumber == currentMax)
            return AiRunHelpers.Fail<ContentRevisionDto>(
                "Revision này đã là phiên bản hiện tại, không cần khôi phục.", ResultErrorCodes.Conflict);

        // ── 5. Tạo revision mới (append-only) với payload từ source ──
        var summary = $"Khôi phục từ v{source.VersionNumber}"
                      + (string.IsNullOrWhiteSpace(cmd.Reason) ? "" : $": {cmd.Reason}");

        var hash = ComputeHash(source.PayloadJson);

        var newRevision = ContentRevision.Create(
            chapterId: source.ChapterId,
            sceneId: source.SceneId,
            kind: source.Kind,
            versionNumber: currentMax + 1,
            summary: summary,
            payloadJson: source.PayloadJson,
            contentHash: hash,
            previousRevisionId: source.Id,       // Trỏ về revision nguồn
            sourceSuggestionId: null,              // Hành động người, không phải AI
            mappingJson: source.MappingJson);

        _db.ContentRevisions.Add(newRevision);
        await _db.SaveChangesAsync(ct);            // AuditLog tự ghi qua DbContext interceptor

        return Result<ContentRevisionDto>.Success(new ContentRevisionDto(
            newRevision.Id, newRevision.Kind, newRevision.VersionNumber,
            newRevision.Summary, newRevision.ContentHash,
            newRevision.PreviousRevisionId, newRevision.SourceSuggestionId,
            AiRunHelpers.ToElement(newRevision.MappingJson), newRevision.CreatedAt));
    }

    private static string ComputeHash(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexStringLower(bytes);
    }
}
