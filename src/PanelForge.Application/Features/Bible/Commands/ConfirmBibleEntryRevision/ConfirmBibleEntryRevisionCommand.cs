using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.Bible.Models;
using PanelForge.Domain.Entities.Audit;
using PanelForge.Domain.Entities.Bible;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Bible.Commands.ConfirmBibleEntryRevision;

/// <summary>
/// V2.6 (Q20): Editor xác nhận = khoá Canon. Chỉ áp dụng từ ConceptSubmitted,
/// Editor phải khác Artist đã nộp Concept (BR-06).
/// BIB-13: Khi bản thay thế được Editor khoá, đánh dấu bản cũ thành Superseded
/// và tính lại bản kế thừa cho các chương chưa Frozen.
/// </summary>
public sealed record ConfirmBibleEntryRevisionCommand(
    Guid SeriesId,
    Guid BibleEntryId,
    Guid RevisionId,
    Guid UserId,
    Guid? ConceptEventId = null
) : IRequest<Result<BibleEntryRevisionDto>>;

public sealed class ConfirmBibleEntryRevisionCommandHandler
    : IRequestHandler<ConfirmBibleEntryRevisionCommand, Result<BibleEntryRevisionDto>>
{
    private readonly IPanelForgeDbContext _dbContext;
    private readonly IChapterCastInheritanceResolver _inheritanceResolver;

    public ConfirmBibleEntryRevisionCommandHandler(
        IPanelForgeDbContext dbContext,
        IChapterCastInheritanceResolver inheritanceResolver)
    {
        _dbContext = dbContext;
        _inheritanceResolver = inheritanceResolver;
    }

    public async Task<Result<BibleEntryRevisionDto>> Handle(ConfirmBibleEntryRevisionCommand cmd, CancellationToken ct)
    {
        var bibleId = await _dbContext.SeriesBibles
            .Where(b => b.SeriesId == cmd.SeriesId)
            .Select(b => (Guid?)b.Id)
            .FirstOrDefaultAsync(ct);

        var entry = bibleId == null
            ? null
            : await _dbContext.BibleEntries
                .Include(e => e.Revisions).ThenInclude(r => r.Events)
                .FirstOrDefaultAsync(e => e.Id == cmd.BibleEntryId && e.SeriesBibleId == bibleId.Value, ct);

        var revision = entry?.Revisions.FirstOrDefault(r => r.Id == cmd.RevisionId);
        if (entry == null || revision == null)
        {
            return Result<BibleEntryRevisionDto>.Failure(
                $"Revision {cmd.RevisionId} của mục Bible {cmd.BibleEntryId} không tồn tại.", ResultErrorCodes.NotFound);
        }

        if (!entry.IsVisualDesign)
        {
            return Result<BibleEntryRevisionDto>.Failure(
                "Chỉ khoá Canon được mục có thiết kế hình (Nhân vật, Địa điểm, Đạo cụ).", ResultErrorCodes.Validation);
        }

        if (revision.IsLocked)
        {
            return Result<BibleEntryRevisionDto>.Failure("Revision này đã được khoá trước đó.", ResultErrorCodes.Conflict);
        }

        if (revision.ReviewStatus != BibleRevisionStatus.ConceptSubmitted)
        {
            return Result<BibleEntryRevisionDto>.Failure(
                $"Chỉ xác nhận được revision đang ở trạng thái ConceptSubmitted, hiện là {revision.ReviewStatus}.", ResultErrorCodes.Conflict);
        }

        var targetConceptEventId = revision.CurrentConceptEventId;
        if (!targetConceptEventId.HasValue)
        {
            return Result<BibleEntryRevisionDto>.Failure("Revision chưa có phiên bản concept nào để xác nhận.", ResultErrorCodes.Conflict);
        }

        if (cmd.ConceptEventId.HasValue && cmd.ConceptEventId.Value != targetConceptEventId.Value)
        {
            return Result<BibleEntryRevisionDto>.Failure(
                "Phiên bản concept xác nhận không phải phiên bản đang nộp mới nhất; Artist vừa nộp bản mới.", ResultErrorCodes.Conflict);
        }

        var seriesInfo = _dbContext.Series is not null
            ? await _dbContext.Series.AsNoTracking()
                .Where(s => s.Id == cmd.SeriesId)
                .Select(s => new { s.AllowSoloCreatorReview, s.WorkspaceId })
                .FirstOrDefaultAsync(ct)
            : null;
        var allowSoloCreatorReview = seriesInfo?.AllowSoloCreatorReview ?? false;

        var currentEvent = revision.Events.FirstOrDefault(e => e.Id == targetConceptEventId.Value);
        var isSelfApproval = (currentEvent?.ActorId.HasValue == true && currentEvent.ActorId.Value == cmd.UserId)
            || (revision.ArtistUserId.HasValue && revision.ArtistUserId.Value == cmd.UserId);
        if (isSelfApproval && !allowSoloCreatorReview)
        {
            return Result<BibleEntryRevisionDto>.Failure(
                "Bạn đã nộp Concept cho phiên bản này; Canon phải do một Editor khác xác nhận (BR-06).", ResultErrorCodes.Forbidden);
        }

        try
        {
            revision.ConfirmCanon(cmd.UserId, targetConceptEventId.Value, allowSoloCreatorReview);

            if (isSelfApproval && allowSoloCreatorReview && _dbContext.AuditLogs is not null)
            {
                _dbContext.AuditLogs.Add(AuditLog.Create(
                    action: "CANON_LOCK_SELF_APPROVED",
                    entityName: nameof(BibleEntryRevision),
                    entityId: revision.Id.ToString(),
                    workspaceId: seriesInfo?.WorkspaceId,
                    userId: cmd.UserId,
                    details: JsonSerializer.Serialize(new Dictionary<string, object>
                    {
                        ["self-approved"] = true,
                        ["bibleEntryId"] = entry.Id,
                        ["revisionId"] = revision.Id,
                        ["versionNumber"] = revision.VersionNumber
                    })));
            }

            var snapshot = BibleEntrySnapshot.From(entry, entry.GetDisplayRevision());
            entry.UpdateDetails(
                snapshot.Description,
                snapshot.DetailsJson,
                snapshot.ReferenceImageUrl);

            // BIB-13 (Q23, Q24): Nếu đây là bản thay thế, đánh dấu bản cũ là Superseded
            if (revision.SupersedesRevisionId.HasValue)
            {
                var oldRevision = entry.Revisions.FirstOrDefault(r => r.Id == revision.SupersedesRevisionId.Value);
                oldRevision?.MarkSuperseded(cmd.UserId, revision.VersionNumber, $"Bị thay bởi v{revision.VersionNumber}");
            }

            // Tính lại kế thừa cho các chương chưa Frozen (binding không do Writer chọn): cả bản thay thế lẫn
            // revision nội dung mới (Artist nộp lại kèm sửa nội dung) — nếu không, chương vẫn trỏ bản nháp cũ
            // và Danh sách thực thể báo "chưa khoá" tới khi Gate 1 tự tính lại.
            await _inheritanceResolver.ResolveInheritedAsync(cmd.SeriesId, 1, ct);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Result<BibleEntryRevisionDto>.Failure(ex.Message, ResultErrorCodes.Forbidden);
        }
        catch (InvalidOperationException ex)
        {
            return Result<BibleEntryRevisionDto>.Failure(ex.Message, ResultErrorCodes.Conflict);
        }

        entry.UpdatedBy = cmd.UserId.ToString();
        await _dbContext.SaveChangesAsync(ct);

        return Result<BibleEntryRevisionDto>.Success(BibleEntryRevisionDto.From(revision));
    }
}
