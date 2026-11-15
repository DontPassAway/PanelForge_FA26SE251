using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Bible.Models;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Domain.Common;
using PanelForge.Domain.Entities.Bible;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Bible.Commands.AddBibleEntryRevision;

public sealed record AddBibleEntryRevisionCommand(
    Guid SeriesId,
    Guid BibleEntryId,
    Guid UserId,
    string Summary,
    string? Name = null,
    string? Description = null,
    string? Subtitle = null,
    string? DetailsJson = null,
    string? ReferenceImageUrl = null,
    BiblePriority Priority = BiblePriority.Standard,
    bool StrictCheck = true,
    // UC-04 luồng phụ: version mà người dùng đã mở để sửa. Nếu khác version mới nhất → xung đột (409).
    int? ExpectedVersionNumber = null,
    // V1.2: Producer thay thế một bản đã khoá (route .../supersede, chỉ Producer). Bản mới cùng chương, được khoá luôn;
    // bản cũ giữ nguyên (không mở khoá, không sửa tại chỗ). Lý do bắt buộc, ghi vào Summary.
    Guid? SupersedesRevisionId = null,
    string? SupersedeReason = null,
    string? StageName = null,
    Guid? ArtistUserId = null,
    BibleRevisionStatus? ReviewStatus = null,
    Guid? ChapterId = null,
    bool IsNewVariant = false,
    Guid? TargetRevisionId = null,
    string? VariantLabel = null
) : IRequest<Result<BibleEntryRevisionDto>>
{
    public string? EffectiveStageName => !string.IsNullOrWhiteSpace(VariantLabel) ? VariantLabel.Trim() : StageName?.Trim();
}

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
                .Include(e => e.Revisions).ThenInclude(r => r.Events)
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

        if (command.Name is not null && !string.Equals(command.Name.Trim(), entry.Name, StringComparison.Ordinal))
        {
            return Result<BibleEntryRevisionDto>.Failure(
                "Tên của mục Bible không thể thay đổi sau khi đã được định nghĩa.", ResultErrorCodes.Validation);
        }

        if (command.Description is not null && string.IsNullOrWhiteSpace(command.Description))
        {
            return Result<BibleEntryRevisionDto>.Failure("Mô tả của mục Bible không được để trống.");
        }

        var displaySnapshot = BibleEntrySnapshot.From(entry, entry.GetDisplayRevision());
        var name = entry.Name;
        var description = string.IsNullOrWhiteSpace(command.Description) ? displaySnapshot.Description : command.Description.Trim();
        var subtitle = command.Subtitle ?? displaySnapshot.Subtitle;
        var detailsJson = string.IsNullOrWhiteSpace(command.DetailsJson) ? displaySnapshot.DetailsJson : command.DetailsJson.Trim();

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(description))
        {
            return Result<BibleEntryRevisionDto>.Failure("Tên và mô tả của mục Bible không được để trống.");
        }

        string? stageName;
        var summary = command.Summary;

        if (command.IsNewVariant)
        {
            var variantLabel = command.EffectiveStageName;
            if (string.IsNullOrWhiteSpace(variantLabel) || string.IsNullOrEmpty(BibleNameNormalizer.Normalize(variantLabel)))
            {
                return Result<BibleEntryRevisionDto>.Failure(
                    "Tên biến thể (variant_label) không được để trống.", ResultErrorCodes.Validation);
            }

            var trimmedLabel = variantLabel.Trim();
            if (trimmedLabel.Length > 100)
            {
                return Result<BibleEntryRevisionDto>.Failure(
                    "Tên biến thể tối đa 100 ký tự.", ResultErrorCodes.Validation);
            }

            if (entry.HasActiveVariant(trimmedLabel))
            {
                return Result<BibleEntryRevisionDto>.Failure(
                    $"Biến thể '{trimmedLabel}' đã tồn tại và đang hoạt động.", ResultErrorCodes.Conflict);
            }

            stageName = trimmedLabel;
        }
        else if (command.SupersedesRevisionId is { } supersededId)
        {
            var superseded = entry.Revisions.FirstOrDefault(r => r.Id == supersededId);
            if (superseded is null)
                return Result<BibleEntryRevisionDto>.Failure(
                    $"Revision {supersededId} của mục Bible {entry.Id} không tồn tại.", ResultErrorCodes.NotFound);
            if (!superseded.IsLocked)
                return Result<BibleEntryRevisionDto>.Failure(
                    "Revision này chưa được khoá, hãy thêm phiên bản mới theo cách thông thường.", ResultErrorCodes.Conflict);
            var reason = command.SupersedeReason?.Trim();
            if (string.IsNullOrEmpty(reason) || reason.Length > MaxSupersedeReasonLength)
                return Result<BibleEntryRevisionDto>.Failure(
                    $"Phải nhập lý do thay thế bản đã khoá (tối đa {MaxSupersedeReasonLength} ký tự).", ResultErrorCodes.Validation);

            // BIB-02, Q23: supersede chỉ thay bản cùng biến thể, cấm đổi variant_label
            if (command.EffectiveStageName is not null)
            {
                var normalizedSent = BibleNameNormalizer.Normalize(command.EffectiveStageName);
                var normalizedSuperseded = BibleNameNormalizer.Normalize(superseded.StageName);
                if (!string.Equals(normalizedSent, normalizedSuperseded, StringComparison.OrdinalIgnoreCase))
                {
                    return Result<BibleEntryRevisionDto>.Failure(
                        "Bản thay thế phải thuộc cùng biến thể với bản được thay thế.", ResultErrorCodes.Conflict);
                }
            }

            stageName = superseded.StageName;
            summary = $"Thay thế bản khoá v{superseded.VersionNumber}: {reason}";
        }
        else
        {
            // Sửa nội dung (Content Edit): không được đổi variant_label (tránh tách biến thể)
            var baseRevision = (command.TargetRevisionId.HasValue
                ? entry.Revisions.FirstOrDefault(r => r.Id == command.TargetRevisionId.Value)
                : null)
                ?? (command.ExpectedVersionNumber.HasValue
                    ? entry.Revisions.FirstOrDefault(r => r.VersionNumber == command.ExpectedVersionNumber.Value)
                    : null)
                ?? entry.GetDisplayRevision();

            var baseStageName = baseRevision?.StageName;
            if (command.EffectiveStageName is not null)
            {
                var normalizedSent = BibleNameNormalizer.Normalize(command.EffectiveStageName);
                var normalizedBase = BibleNameNormalizer.Normalize(baseStageName);
                if (!string.Equals(normalizedSent, normalizedBase, StringComparison.OrdinalIgnoreCase))
                {
                    return Result<BibleEntryRevisionDto>.Failure(
                        "Không thể thay đổi tên biến thể (variant_label) khi sửa nội dung. Để tạo biến thể mới, vui lòng dùng thao tác tạo biến thể.",
                        ResultErrorCodes.Conflict);
                }
            }

            stageName = baseStageName;
        }

        var snapshotObj = new
        {
            entry.Id,
            entry.Code,
            Name = name,
            Subtitle = subtitle?.Trim(),
            Description = description,
            DetailsJson = detailsJson,
            ReferenceImageUrl = command.ReferenceImageUrl?.Trim(),
            command.Priority,
            command.StrictCheck,
            StageName = stageName?.Trim()
        };
        var snapshotJson = JsonSerializer.Serialize(snapshotObj);
        var contentHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(snapshotJson)));

        var reviewStatus = command.SupersedesRevisionId.HasValue
            ? BibleRevisionStatus.Draft
            : (command.ReviewStatus is BibleRevisionStatus.ConceptSubmitted or null ? BibleRevisionStatus.Draft : command.ReviewStatus.Value);

        var revision = entry.AddRevision(
            summary: summary,
            snapshotJson: snapshotJson,
            contentHash: contentHash,
            authorId: command.UserId,
            associatedChapterId: command.ChapterId,
            isInitialVersion: false,
            stageName: stageName,
            artistUserId: command.ArtistUserId,
            reviewStatus: reviewStatus,
            supersedesRevisionId: command.SupersedesRevisionId
        );
        // Artist nộp Concept kèm sửa nội dung: phiên bản mới bắt đầu Draft rồi ghi sự kiện nộp Concept như mọi lần nộp khác
        if (command.ArtistUserId is { } artistId && !string.IsNullOrWhiteSpace(command.ReferenceImageUrl))
            revision.SubmitConcept(artistId, command.ReferenceImageUrl, summary);

        // BIB-09, Q23: Bản thay thế KHÔNG khoá ngay và KHÔNG đánh dấu bản cũ Superseded sớm.
        // Bản cũ vẫn CanonLocked dùng được. Khi Editor khoá bản mới (BIB-10 ConfirmCanon) mới ghi sự kiện Superseded cho bản cũ.
        // Nếu tạo từ màn kịch bản chương N thì gắn luôn bản mới cho chương N (chosen_by_writer = true).
        if (command.SupersedesRevisionId is not null && command.ChapterId is { } chapterId)
        {
            var binding = await _dbContext.ChapterBibleBindings
                .FirstOrDefaultAsync(b => b.ChapterId == chapterId && b.BibleEntryId == entry.Id, cancellationToken);
            if (binding != null)
            {
                binding.UpdateBinding(revision.Id, binding.Source, chosenByWriter: true, boundById: command.UserId);
            }
            else
            {
                var newBinding = ChapterBibleBinding.Create(
                    chapterId: chapterId,
                    bibleEntryId: entry.Id,
                    boundRevisionId: revision.Id,
                    source: ChapterBibleBindingSource.Speaker,
                    chosenByWriter: true,
                    isCrowdAtBind: entry.IsCrowd,
                    boundById: command.UserId
                );
                _dbContext.ChapterBibleBindings.Add(newBinding);
            }
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

        return Result<BibleEntryRevisionDto>.Success(BibleEntryRevisionDto.From(revision));
    }

    // Summary là varchar(500): chừa chỗ cho tiền tố "Thay thế bản khoá vN: "
    private const int MaxSupersedeReasonLength = 400;

    private static bool IsVersionUniqueViolation(DbUpdateException ex)
        => ex.InnerException?.Message.Contains("ix_bible_entry_revisions_bible_entry_id_version", StringComparison.OrdinalIgnoreCase) == true;
}
