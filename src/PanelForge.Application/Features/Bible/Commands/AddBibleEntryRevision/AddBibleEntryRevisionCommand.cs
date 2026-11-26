using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Bible.Common;
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
    string? DetailsJson = null,
    string? ReferenceImageUrl = null,
    int? ExpectedVersionNumber = null,
    Guid? SupersedesRevisionId = null,
    string? SupersedeReason = null,
    string? StageName = null,
    Guid? ArtistUserId = null,
    BibleRevisionStatus? ReviewStatus = null,
    Guid? ChapterId = null,
    bool IsNewVariant = false,
    Guid? TargetRevisionId = null,
    string? Age = null,
    string? ArtistNotes = null,
    string? Subtitle = null,
    BiblePriority Priority = BiblePriority.Standard,
    bool StrictCheck = true,
    string? VariantLabel = null
) : IRequest<Result<BibleEntryRevisionDto>>
{
    public string? EffectiveStageName => !string.IsNullOrWhiteSpace(StageName) ? StageName.Trim() : VariantLabel?.Trim();
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
        var bibleId = await _dbContext.SeriesBibles
            .Where(b => b.SeriesId == command.SeriesId)
            .Select(b => (Guid?)b.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (bibleId == null)
        {
            return Result<BibleEntryRevisionDto>.Failure(
                $"Series {command.SeriesId} không tồn tại hoặc chưa có Series Bible.", ResultErrorCodes.NotFound);
        }

        var entry = await _dbContext.BibleEntries
            .Include(e => e.Revisions)
                .ThenInclude(r => r.Events)
            .FirstOrDefaultAsync(e => e.Id == command.BibleEntryId, cancellationToken);

        if (entry == null)
        {
            return Result<BibleEntryRevisionDto>.Failure(
                $"Mục Bible {command.BibleEntryId} không tồn tại.", ResultErrorCodes.NotFound);
        }

        if (entry.SeriesBibleId != bibleId.Value)
        {
            return Result<BibleEntryRevisionDto>.Failure(
                $"Mục Bible {command.BibleEntryId} không thuộc Series {command.SeriesId}.", ResultErrorCodes.NotFound);
        }

        var resolvedBibleId = bibleId.Value;

        var latestVersion = entry.Revisions.Max(r => (int?)r.VersionNumber) ?? 0;
        if (command.ExpectedVersionNumber.HasValue && command.ExpectedVersionNumber.Value != latestVersion)
        {
            return Result<BibleEntryRevisionDto>.Failure(
                $"Xung đột phiên bản: bạn đang sửa từ version {command.ExpectedVersionNumber.Value} nhưng mục này đã có version {latestVersion}. " +
                "Vui lòng tải lại nội dung mới nhất, đối chiếu thay đổi rồi gửi lại.",
                ResultErrorCodes.Conflict);
        }

        if (command.Name is not null && !string.Equals(command.Name.Trim(), entry.Name, StringComparison.Ordinal))
        {
            var trimmedNewName = command.Name.Trim();
            if (string.IsNullOrWhiteSpace(trimmedNewName))
            {
                return Result<BibleEntryRevisionDto>.Failure("Tên của mục Bible không được để trống.");
            }

            if (entry.HasLockedRevision)
            {
                return Result<BibleEntryRevisionDto>.Failure(
                    "Tên mục Bible không thể thay đổi sau khi đã có phiên bản khoá Canon (BR-02).",
                    ResultErrorCodes.Validation);
            }

            var normalizedNewName = BibleNameNormalizer.Normalize(trimmedNewName);
            var siblings = await _dbContext.BibleEntries
                .AsNoTracking()
                .Where(e => e.SeriesBibleId == resolvedBibleId && e.Id != entry.Id)
                .Select(e => new { e.Name, e.NormalizedName })
                .ToListAsync(cancellationToken);

            var conflict = siblings.FirstOrDefault(s =>
                string.Equals(s.NormalizedName, normalizedNewName, StringComparison.OrdinalIgnoreCase));
            if (conflict != null)
            {
                return Result<BibleEntryRevisionDto>.Failure(
                    $"Tên '{trimmedNewName}' đã trùng với mục '{conflict.Name}' trong Series Bible.",
                    ResultErrorCodes.Conflict);
            }

            entry.RenameBeforeFirstLock(trimmedNewName);
        }

        if (command.Description is not null && string.IsNullOrWhiteSpace(command.Description))
        {
            return Result<BibleEntryRevisionDto>.Failure("Mô tả của mục Bible không được để trống.");
        }

        var displaySnapshot = BibleEntrySnapshot.From(entry, entry.GetDisplayRevision());
        var name = entry.Name;
        var description = string.IsNullOrWhiteSpace(command.Description) ? displaySnapshot.Description : command.Description.Trim();
        if (!string.IsNullOrWhiteSpace(command.Subtitle))
        {
            description = $"({command.Subtitle.Trim()}) {description}";
        }

        string detailsJson;
        if (!string.IsNullOrWhiteSpace(command.Age) || !string.IsNullOrWhiteSpace(command.ArtistNotes))
        {
            detailsJson = BibleDetailsParser.Serialize(
                command.Age ?? displaySnapshot.Age,
                command.ArtistNotes ?? displaySnapshot.ArtistNotes);
        }
        else if (!string.IsNullOrWhiteSpace(command.DetailsJson))
        {
            detailsJson = BibleDetailsParser.NormalizeJson(command.DetailsJson);
        }
        else
        {
            detailsJson = displaySnapshot.DetailsJson;
        }

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(description))
        {
            return Result<BibleEntryRevisionDto>.Failure("Tên và mô tả của mục Bible không được để trống.");
        }

        string? stageName;
        var summary = command.Summary;

        if (command.IsNewVariant)
        {
            var rawLabel = command.EffectiveStageName;
            if (string.IsNullOrWhiteSpace(rawLabel) || string.IsNullOrEmpty(BibleNameNormalizer.Normalize(rawLabel)))
            {
                return Result<BibleEntryRevisionDto>.Failure(
                    "Tên biến thể mới (stage_name / variant_label) không được để trống.", ResultErrorCodes.Validation);
            }

            var cleanLabel = rawLabel.Trim();
            if (cleanLabel.Length > 100)
            {
                return Result<BibleEntryRevisionDto>.Failure(
                    "Tên biến thể tối đa 100 ký tự.", ResultErrorCodes.Validation);
            }

            if (entry.HasActiveVariant(cleanLabel))
            {
                return Result<BibleEntryRevisionDto>.Failure(
                    $"Biến thể '{cleanLabel}' đã tồn tại và đang hoạt động trong mục Bible '{entry.Name}'.", ResultErrorCodes.Conflict);
            }

            stageName = cleanLabel;
            if (string.IsNullOrWhiteSpace(summary))
                summary = $"Tạo biến thể mới: {stageName}";
        }
        else if (command.SupersedesRevisionId.HasValue)
        {
            var superseded = entry.Revisions.FirstOrDefault(r => r.Id == command.SupersedesRevisionId.Value);
            if (superseded == null)
            {
                return Result<BibleEntryRevisionDto>.Failure(
                    $"Revision {command.SupersedesRevisionId.Value} không tồn tại trong mục Bible {entry.Id}.",
                    ResultErrorCodes.NotFound);
            }

            if (!superseded.IsLocked)
            {
                return Result<BibleEntryRevisionDto>.Failure(
                    $"Chỉ có thể thay thế (supersede) một phiên bản đã khoá Canon. Bản v{superseded.VersionNumber} đang ở trạng thái {superseded.ReviewStatus}.",
                    ResultErrorCodes.Conflict);
            }

            if (entry.IsRevisionSuperseded(superseded))
            {
                return Result<BibleEntryRevisionDto>.Failure(
                    $"Bản khoá v{superseded.VersionNumber} đã được thay thế trước đó.",
                    ResultErrorCodes.Conflict);
            }

            var reason = command.SupersedeReason?.Trim();
            if (string.IsNullOrWhiteSpace(reason))
            {
                return Result<BibleEntryRevisionDto>.Failure(
                    "Lý do thay thế bản khoá là bắt buộc.", ResultErrorCodes.Validation);
            }

            if (!string.IsNullOrWhiteSpace(command.EffectiveStageName) &&
                !string.Equals(command.EffectiveStageName.Trim(), superseded.StageName ?? string.Empty, StringComparison.OrdinalIgnoreCase))
            {
                return Result<BibleEntryRevisionDto>.Failure(
                    $"Bản thay thế phải thuộc cùng biến thể ('{superseded.StageName ?? "mặc định"}').",
                    ResultErrorCodes.Conflict);
            }

            stageName = superseded.StageName;
            summary = $"Thay thế bản khoá v{superseded.VersionNumber}: {reason}";
        }
        else
        {
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
                        "Không thể thay đổi tên biến thể khi sửa nội dung. Để tạo biến thể mới, vui lòng dùng thao tác tạo biến thể.",
                        ResultErrorCodes.Conflict);
                }
            }

            stageName = baseStageName;
        }

        var snapshotObj = new
        {
            entry.Id,
            Name = name,
            Description = description,
            DetailsJson = detailsJson,
            ReferenceImageUrl = command.ReferenceImageUrl?.Trim(),
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
                _dbContext.ChapterBibleBindings.Add(
                    ChapterBibleBinding.Create(chapterId, entry.Id, revision.Id, ChapterBibleBindingSource.Highlight, chosenByWriter: true, isCrowdAtBind: entry.IsCrowd, boundById: command.UserId));
            }
        }

        entry.UpdatedBy = command.UserId.ToString();
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result<BibleEntryRevisionDto>.Success(BibleEntryRevisionDto.From(revision));
    }
}
