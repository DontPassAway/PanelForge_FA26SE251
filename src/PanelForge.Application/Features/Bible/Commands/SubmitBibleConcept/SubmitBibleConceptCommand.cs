using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Bible.Commands.AddBibleEntryRevision;
using PanelForge.Application.Features.Bible.Models;
using PanelForge.Domain.Common;
using PanelForge.Domain.Entities.Bible;

namespace PanelForge.Application.Features.Bible.Commands.SubmitBibleConcept;

/// <summary>
/// Artist nộp bản vẽ Concept cho phiên bản hiện hành của mục Bible. Chỉ nộp ảnh → thêm sự kiện ConceptSubmitted vào chính
/// phiên bản đó (không tăng VersionNumber). Artist sửa cả nội dung (mô tả/đặc tả/giai đoạn/chương hiệu lực) → đó là
/// thay đổi nội dung thật, đi đường thêm phiên bản như cũ. Phiên bản đã khoá Canon → 409, phải Supersede.
/// </summary>
public sealed record SubmitBibleConceptCommand(
    Guid SeriesId,
    Guid BibleEntryId,
    Guid ArtistUserId,
    SubmitConceptArtRequest Body
) : IRequest<Result<BibleEntryRevisionDto>>;

public sealed class SubmitBibleConceptCommandHandler : IRequestHandler<SubmitBibleConceptCommand, Result<BibleEntryRevisionDto>>
{
    private readonly IPanelForgeDbContext _db;
    private readonly ISender _sender;

    public SubmitBibleConceptCommandHandler(IPanelForgeDbContext db, ISender sender)
    {
        _db = db;
        _sender = sender;
    }

    public async Task<Result<BibleEntryRevisionDto>> Handle(SubmitBibleConceptCommand cmd, CancellationToken ct)
    {
        var body = cmd.Body;
        if (string.IsNullOrWhiteSpace(body.EffectiveImageUrl))
            return Result<BibleEntryRevisionDto>.Failure("Thiếu ảnh bản vẽ Concept.", ResultErrorCodes.Validation);

        // Entry phải thuộc SeriesBible của seriesId trên route (chống IDOR, như AddBibleEntryRevision)
        var bibleId = await _db.SeriesBibles
            .Where(b => b.SeriesId == cmd.SeriesId)
            .Select(b => (Guid?)b.Id)
            .FirstOrDefaultAsync(ct);

        var entry = bibleId is null
            ? null
            : await _db.BibleEntries
                .Include(e => e.Revisions).ThenInclude(r => r.Events)
                .FirstOrDefaultAsync(e => e.Id == cmd.BibleEntryId && e.SeriesBibleId == bibleId.Value, ct);
        if (entry is null)
            return Result<BibleEntryRevisionDto>.Failure($"Mục BibleEntry {cmd.BibleEntryId} không tồn tại.", ResultErrorCodes.NotFound);

        // V2.6 (BIB-10 / 4A): Chặn nộp concept khi mục còn là nháp hoặc mô tả rỗng
        if (entry.IsStub || string.IsNullOrWhiteSpace(entry.Description))
            return Result<BibleEntryRevisionDto>.Failure("Thực thể chưa được Writer viết định nghĩa.", ResultErrorCodes.Conflict);

        // Nhắm đúng giai đoạn: RevisionId tường minh; mặc định là giai đoạn hiện hành.
        BibleEntryRevision? target;
        if (body.RevisionId is { } revisionId)
        {
            target = entry.Revisions.FirstOrDefault(r => r.Id == revisionId);
            if (target is null)
                return Result<BibleEntryRevisionDto>.Failure($"Revision {revisionId} không tồn tại.", ResultErrorCodes.NotFound);
            var normStage = BibleNameNormalizer.Normalize(target.StageName);
            var latestOfStage = entry.Revisions
                .Where(r => string.Equals(BibleNameNormalizer.Normalize(r.StageName), normStage, StringComparison.OrdinalIgnoreCase))
                .MaxBy(r => r.VersionNumber);
            if (latestOfStage != null && latestOfStage != target)
                return Result<BibleEntryRevisionDto>.Failure(
                    $"v{target.VersionNumber} không còn là bản mới nhất của giai đoạn; hãy nộp cho bản mới nhất.", ResultErrorCodes.Conflict);
        }
        else
        {
            target = entry.GetDisplayRevision();
        }

        var targetStageNorm = target != null ? BibleNameNormalizer.Normalize(target.StageName) : "";
        var latestVersion = target != null
            ? entry.Revisions
                .Where(r => string.Equals(BibleNameNormalizer.Normalize(r.StageName), targetStageNorm, StringComparison.OrdinalIgnoreCase))
                .Select(r => r.VersionNumber)
                .DefaultIfEmpty(0)
                .Max()
            : 0;

        if (body.ExpectedVersionNumber is { } expected && expected != latestVersion)
            return Result<BibleEntryRevisionDto>.Failure(
                $"Xung đột phiên bản: bạn đang sửa từ version {expected} nhưng biến thể '{target?.StageName}' đã có version {latestVersion}. " +
                "Vui lòng tải lại nội dung mới nhất rồi gửi lại.",
                ResultErrorCodes.Conflict);

        if (target is null || ChangesContent(entry, target, body))
            return await _sender.Send(new AddBibleEntryRevisionCommand(
                SeriesId: cmd.SeriesId,
                BibleEntryId: cmd.BibleEntryId,
                UserId: cmd.ArtistUserId,
                Summary: body.EffectiveSummary,
                Name: body.Name,
                Description: body.Description,
                DetailsJson: body.EffectiveDetailsJson,
                ReferenceImageUrl: body.EffectiveImageUrl,
                StageName: body.EffectiveStageName,
                ExpectedVersionNumber: body.ExpectedVersionNumber,
                ArtistUserId: cmd.ArtistUserId,
                TargetRevisionId: target?.Id,
                Age: body.Age,
                ArtistNotes: body.ArtistNotes), ct);

        if (target.IsLocked)
            return Result<BibleEntryRevisionDto>.Failure(
                $"Phiên bản v{target.VersionNumber} đã khoá Canon. Muốn đổi thiết kế, Producer phải dùng Thay thế (supersede).",
                ResultErrorCodes.Conflict);

        target.SubmitConcept(cmd.ArtistUserId, body.EffectiveImageUrl, body.EffectiveSummary, body.AssetSha256);
        entry.UpdatedBy = cmd.ArtistUserId.ToString();
        await _db.SaveChangesAsync(ct);

        return Result<BibleEntryRevisionDto>.Success(BibleEntryRevisionDto.From(target));
    }

    /// <summary>
    /// Artist có gửi kèm nội dung khác snapshot của phiên bản đích không (trường null/rỗng = giữ nguyên).
    /// </summary>
    private static bool ChangesContent(BibleEntry entry, BibleEntryRevision target, SubmitConceptArtRequest b)
    {
        static bool Differs(string? sent, string? current) =>
            !string.IsNullOrWhiteSpace(sent) && sent.Trim() != (current ?? "").Trim();

        var snapshot = BibleEntrySnapshot.From(entry, target);

        return Differs(b.Name, entry.Name)
            || Differs(b.Description, snapshot.Description)
            || Differs(b.DetailsJson, snapshot.DetailsJson)
            || Differs(b.EffectiveStageName, target.StageName);
    }
}
