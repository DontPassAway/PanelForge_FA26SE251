using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Bible.Commands.AddBibleEntryRevision;
using PanelForge.Application.Features.Bible.Models;
using PanelForge.Domain.Entities.Bible;

namespace PanelForge.Application.Features.Bible.Commands.SubmitBibleConcept;

/// <summary>
/// Artist nộp bản vẽ Concept cho phiên bản hiện hành của mục Bible. Chỉ nộp ảnh → thêm sự kiện ConceptSubmitted vào chính
/// phiên bản đó (không tăng VersionNumber). Artist sửa cả nội dung (tên/mô tả/đặc tả/giai đoạn/chương hiệu lực) → đó là
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
        var entry = await _db.BibleEntries
            .Include(e => e.Revisions).ThenInclude(r => r.Events)
            .FirstOrDefaultAsync(e => e.Id == cmd.BibleEntryId && e.SeriesBible.SeriesId == cmd.SeriesId, ct);
        if (entry is null)
            return Result<BibleEntryRevisionDto>.Failure($"Mục BibleEntry {cmd.BibleEntryId} không tồn tại.", ResultErrorCodes.NotFound);

        var latestVersion = entry.Revisions.Count > 0 ? entry.Revisions.Max(r => r.VersionNumber) : 0;
        if (body.ExpectedVersionNumber is { } expected && expected != latestVersion)
            return Result<BibleEntryRevisionDto>.Failure(
                $"Xung đột phiên bản: bạn đang sửa từ version {expected} nhưng mục này đã có version {latestVersion}. " +
                "Vui lòng tải lại nội dung mới nhất rồi gửi lại.",
                ResultErrorCodes.Conflict);

        // Nhắm đúng giai đoạn: RevisionId tường minh, hoặc giai đoạn bắt đầu ở chương được gửi lên; mặc định là giai đoạn hiện hành.
        // Trước đây luôn lấy giai đoạn mới nhất nên giai đoạn cũ (vd. "Lúc nhỏ") không bao giờ nộp được Concept.
        BibleEntryRevision? target;
        if (body.RevisionId is { } revisionId)
        {
            target = entry.Revisions.FirstOrDefault(r => r.Id == revisionId);
            if (target is null)
                return Result<BibleEntryRevisionDto>.Failure($"Revision {revisionId} không tồn tại.", ResultErrorCodes.NotFound);
            if (entry.LatestRevisionOfStageStartingAt(target.EffectiveFromChapterNumber) != target)
                return Result<BibleEntryRevisionDto>.Failure(
                    $"v{target.VersionNumber} không còn là bản mới nhất của giai đoạn; hãy nộp cho bản mới nhất.", ResultErrorCodes.Conflict);
        }
        else
        {
            target = (body.EffectiveFromChapterNumber is { } from ? entry.LatestRevisionOfStageStartingAt(from) : null)
                ?? entry.GetCurrentRevision();
        }

        var isCurrent = target is not null && entry.GetCurrentRevision() == target;
        if (target is null || ChangesContent(entry, target, isCurrent, body))
            return await _sender.Send(new AddBibleEntryRevisionCommand(
                SeriesId: cmd.SeriesId,
                BibleEntryId: cmd.BibleEntryId,
                UserId: cmd.ArtistUserId,
                Summary: body.EffectiveSummary,
                Name: body.Name,
                Description: body.Description,
                Subtitle: body.Subtitle,
                DetailsJson: body.DetailsJson,
                ReferenceImageUrl: body.EffectiveImageUrl,
                StageName: body.StageName,
                EffectiveFromChapterNumber: body.EffectiveFromChapterNumber ?? target?.EffectiveFromChapterNumber ?? 1,
                EffectiveToChapterNumber: body.EffectiveToChapterNumber,
                ExpectedVersionNumber: body.ExpectedVersionNumber,
                ArtistUserId: cmd.ArtistUserId), ct);

        if (target.IsLocked)
            return Result<BibleEntryRevisionDto>.Failure(
                $"Phiên bản v{target.VersionNumber} đã khoá Canon. Muốn đổi thiết kế, Producer phải dùng Thay thế (supersede).",
                ResultErrorCodes.Conflict);

        target.SubmitConcept(cmd.ArtistUserId, body.EffectiveImageUrl, body.EffectiveSummary);
        // Ảnh hiện hành của mục = Concept mới nhất của phiên bản hiện hành; Concept của giai đoạn cũ không đổi ảnh hiện hành
        if (isCurrent)
            entry.UpdateDetails(entry.Name, entry.Description, entry.Subtitle, entry.DetailsJson,
                body.EffectiveImageUrl, entry.Priority, entry.StrictCheck);
        entry.UpdatedBy = cmd.ArtistUserId.ToString();
        await _db.SaveChangesAsync(ct);

        return Result<BibleEntryRevisionDto>.Success(BibleEntryRevisionDto.From(target));
    }

    /// <summary>
    /// Artist có gửi kèm nội dung khác phiên bản đích không (trường null/rỗng = giữ nguyên). Đích là giai đoạn hiện hành thì so với
    /// current state của mục; giai đoạn cũ thì so với snapshot của chính phiên bản đó.
    /// </summary>
    private static bool ChangesContent(BibleEntry entry, BibleEntryRevision target, bool isCurrent, SubmitConceptArtRequest b)
    {
        static bool Differs(string? sent, string? current) =>
            !string.IsNullOrWhiteSpace(sent) && sent.Trim() != (current ?? "").Trim();

        var (name, description, subtitle, detailsJson) = isCurrent
            ? (entry.Name, entry.Description, entry.Subtitle, entry.DetailsJson)
            : ReadSnapshot(target.SnapshotJson, entry);

        return Differs(b.Name, name)
            || Differs(b.Description, description)
            || Differs(b.Subtitle, subtitle)
            || Differs(b.DetailsJson, detailsJson)
            || Differs(b.StageName, target.StageName)
            || (b.EffectiveFromChapterNumber is { } from && from != target.EffectiveFromChapterNumber)
            || (b.EffectiveToChapterNumber is { } to && to != target.EffectiveToChapterNumber);
    }

    private static (string? Name, string? Description, string? Subtitle, string? DetailsJson) ReadSnapshot(string snapshotJson, BibleEntry fallback)
    {
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(snapshotJson) ? "{}" : snapshotJson);
        var root = doc.RootElement;
        string? Read(string key, string? current) =>
            root.ValueKind == JsonValueKind.Object && root.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString()
                : current;

        return (Read("Name", fallback.Name), Read("Description", fallback.Description),
            Read("Subtitle", fallback.Subtitle), Read("DetailsJson", fallback.DetailsJson));
    }
}
