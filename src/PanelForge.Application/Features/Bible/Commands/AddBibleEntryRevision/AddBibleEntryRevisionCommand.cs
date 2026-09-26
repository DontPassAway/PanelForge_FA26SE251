using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Bible.Models;
using PanelForge.Application.Interfaces.Persistence;
using PanelForge.Domain.Entities.Bible;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Bible.Commands.AddBibleEntryRevision;

public sealed record AddBibleEntryRevisionCommand(
    Guid SeriesId,
    Guid BibleEntryId,
    Guid UserId,
    string Summary,
    string Name,
    string Description,
    string? Subtitle = null,
    string? DetailsJson = null,
    string? ReferenceImageUrl = null,
    BiblePriority Priority = BiblePriority.Standard,
    bool StrictCheck = true,
    int EffectiveFromChapterNumber = 1,
    // UC-04 luồng phụ: version mà người dùng đã mở để sửa. Nếu khác version mới nhất → xung đột (409).
    int? ExpectedVersionNumber = null
) : IRequest<Result<BibleEntryRevisionDto>>;

public sealed class AddBibleEntryRevisionCommandHandler : IRequestHandler<AddBibleEntryRevisionCommand, Result<BibleEntryRevisionDto>>
{
    private readonly IPanelForgeDbContext _dbContext;

    public AddBibleEntryRevisionCommandHandler(IPanelForgeDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<BibleEntryRevisionDto>> Handle(AddBibleEntryRevisionCommand command, CancellationToken cancellationToken)
    {
        // Entry phải thuộc đúng SeriesBible của seriesId trên route: quyền đã được filter kiểm tra theo seriesId,
        // nên không được phép thao tác entry của Series/Workspace khác (chống IDOR).
        var bibleId = await _dbContext.SeriesBibles
            .Where(b => b.SeriesId == command.SeriesId)
            .Select(b => (Guid?)b.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var entry = bibleId == null
            ? null
            : await _dbContext.BibleEntries
                .Include(e => e.Revisions)
                .FirstOrDefaultAsync(e => e.Id == command.BibleEntryId && e.SeriesBibleId == bibleId.Value, cancellationToken);

        if (entry == null)
        {
            return Result<BibleEntryRevisionDto>.Failure($"Mục BibleEntry {command.BibleEntryId} không tồn tại.", ResultErrorCodes.NotFound);
        }

        // UC-04: phát hiện chỉnh sửa đồng thời (optimistic concurrency)
        var latestVersion = entry.Revisions.Count > 0 ? entry.Revisions.Max(r => r.VersionNumber) : 0;
        if (command.ExpectedVersionNumber.HasValue && command.ExpectedVersionNumber.Value != latestVersion)
        {
            return Result<BibleEntryRevisionDto>.Failure(
                $"Xung đột phiên bản: bạn đang sửa từ version {command.ExpectedVersionNumber.Value} nhưng mục này đã có version {latestVersion}. " +
                "Vui lòng tải lại nội dung mới nhất, đối chiếu thay đổi rồi gửi lại.",
                ResultErrorCodes.Conflict);
        }

        if (string.IsNullOrWhiteSpace(command.Name) || string.IsNullOrWhiteSpace(command.Description))
        {
            return Result<BibleEntryRevisionDto>.Failure("Tên và mô tả của mục Bible không được để trống.");
        }

        var effectiveFrom = Math.Max(1, command.EffectiveFromChapterNumber);

        // Snapshot lấy từ nội dung của CHÍNH revision này, không lấy từ current state của entry,
        // vì bản sửa hồi tố không được thay đổi current state (Append-only BR-01, BR-15).
        var snapshotObj = new
        {
            entry.Id,
            entry.Code,
            Name = command.Name.Trim(),
            Subtitle = command.Subtitle?.Trim(),
            Description = command.Description.Trim(),
            DetailsJson = string.IsNullOrWhiteSpace(command.DetailsJson) ? "{}" : command.DetailsJson.Trim(),
            ReferenceImageUrl = command.ReferenceImageUrl?.Trim(),
            command.Priority,
            command.StrictCheck,
            EffectiveFromChapterNumber = effectiveFrom
        };
        var snapshotJson = JsonSerializer.Serialize(snapshotObj);
        var contentHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(snapshotJson)));

        var revision = entry.AddRevision(
            summary: command.Summary,
            snapshotJson: snapshotJson,
            contentHash: contentHash,
            authorId: command.UserId,
            associatedChapterId: null,
            effectiveFromChapterNumber: effectiveFrom,
            isInitialVersion: false
        );

        // Chỉ cập nhật current state khi revision mới là bản có hiệu lực ở chương mới nhất.
        // Bản sửa hồi tố (effective từ chương cũ hơn) chỉ được lưu thành version, current state giữ nguyên.
        if (entry.GetCurrentRevision() == revision)
        {
            entry.UpdateDetails(
                name: command.Name,
                description: command.Description,
                subtitle: command.Subtitle,
                detailsJson: command.DetailsJson,
                referenceImageUrl: command.ReferenceImageUrl,
                priority: command.Priority,
                strictCheck: command.StrictCheck
            );
        }
        entry.UpdatedBy = command.UserId.ToString();

        _dbContext.BibleEntryRevisions.Add(revision);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsVersionUniqueViolation(ex))
        {
            // Hai người lưu cùng lúc: cả hai cùng tính ra version N+1, unique index (entry, version) chặn người thứ hai.
            return Result<BibleEntryRevisionDto>.Failure(
                "Xung đột phiên bản: một người khác vừa lưu thay đổi cho mục này. Vui lòng tải lại và thử lại.",
                ResultErrorCodes.Conflict);
        }

        return Result<BibleEntryRevisionDto>.Success(new BibleEntryRevisionDto(
            Id: revision.Id,
            BibleEntryId: revision.BibleEntryId,
            VersionNumber: revision.VersionNumber,
            Summary: revision.Summary,
            SnapshotJson: revision.SnapshotJson,
            ContentHash: revision.ContentHash,
            AuthorId: revision.AuthorId,
            AssociatedChapterId: revision.AssociatedChapterId,
            EffectiveFromChapterNumber: revision.EffectiveFromChapterNumber,
            IsInitialVersion: revision.IsInitialVersion,
            CreatedAt: revision.CreatedAt
        ));
    }

    private static bool IsVersionUniqueViolation(DbUpdateException ex)
        => ex.InnerException?.Message.Contains("ix_bible_entry_revisions_bible_entry_id_version", StringComparison.OrdinalIgnoreCase) == true;
}
