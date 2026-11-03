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

        var target = entry.GetCurrentRevision();
        if (target is null || ChangesContent(entry, target, body))
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
        // Ảnh hiện hành của mục = Concept mới nhất của phiên bản hiện hành; nội dung khác giữ nguyên
        entry.UpdateDetails(entry.Name, entry.Description, entry.Subtitle, entry.DetailsJson,
            body.EffectiveImageUrl, entry.Priority, entry.StrictCheck);
        entry.UpdatedBy = cmd.ArtistUserId.ToString();
        await _db.SaveChangesAsync(ct);

        return Result<BibleEntryRevisionDto>.Success(BibleEntryRevisionDto.From(target));
    }

    /// <summary>Artist có gửi kèm nội dung khác bản hiện hành không (trường null/rỗng = giữ nguyên).</summary>
    private static bool ChangesContent(BibleEntry entry, BibleEntryRevision target, SubmitConceptArtRequest b)
    {
        static bool Differs(string? sent, string? current) =>
            !string.IsNullOrWhiteSpace(sent) && sent.Trim() != (current ?? "").Trim();

        return Differs(b.Name, entry.Name)
            || Differs(b.Description, entry.Description)
            || Differs(b.Subtitle, entry.Subtitle)
            || Differs(b.DetailsJson, entry.DetailsJson)
            || Differs(b.StageName, target.StageName)
            || (b.EffectiveFromChapterNumber is { } from && from != target.EffectiveFromChapterNumber)
            || (b.EffectiveToChapterNumber is { } to && to != target.EffectiveToChapterNumber);
    }
}
