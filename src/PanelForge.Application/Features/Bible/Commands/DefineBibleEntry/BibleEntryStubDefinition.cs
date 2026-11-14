using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Bible.Models;
using PanelForge.Domain.Common;
using PanelForge.Domain.Entities.Bible;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Bible.Commands.DefineBibleEntry;

internal sealed record BibleEntryDefinition(
    Guid UserId,
    string Name,
    string Description,
    string? Subtitle,
    string? DetailsJson,
    string? ReferenceImageUrl,
    BiblePriority Priority,
    bool StrictCheck,
    string? Code,
    bool? IsCrowd = null);

internal static class BibleEntryStubDefinition
{
    internal static async Task<Result<BibleEntryDto>> DefineAsync(
        IPanelForgeDbContext dbContext,
        SeriesBible bible,
        BibleEntry entry,
        BibleEntryDefinition definition,
        CancellationToken cancellationToken)
    {
        if (!entry.IsStub)
        {
            return Result<BibleEntryDto>.Failure(
                "Mục này đã được định nghĩa trước đó, không phải mục nháp (IsStub = false).", ResultErrorCodes.Conflict);
        }

        if (string.IsNullOrWhiteSpace(definition.Description))
        {
            return Result<BibleEntryDto>.Failure(
                "Mô tả định nghĩa thực thể bắt buộc không được để trống.", ResultErrorCodes.Validation);
        }

        if (string.IsNullOrWhiteSpace(definition.Name))
        {
            return Result<BibleEntryDto>.Failure(
                "Tên thực thể bắt buộc không được để trống.", ResultErrorCodes.Validation);
        }

        var normalizedPrimary = BibleNameNormalizer.Normalize(definition.Name);
        var existingPrimary = await dbContext.BibleEntries
            .FirstOrDefaultAsync(
                e => e.SeriesBibleId == bible.Id && e.NormalizedName == normalizedPrimary && e.Id != entry.Id,
                cancellationToken);

        if (existingPrimary != null)
        {
            return Result<BibleEntryDto>.Failure(
                $"Tên '{definition.Name}' đã trùng với mục '{existingPrimary.Name}' trong Series Bible.",
                ResultErrorCodes.Conflict);
        }

        string finalCode;
        if (!string.IsNullOrWhiteSpace(definition.Code))
        {
            finalCode = definition.Code.Trim().ToUpperInvariant();
            var codeConflict = await dbContext.BibleEntries
                .AnyAsync(e => e.SeriesBibleId == bible.Id && e.Id != entry.Id && e.Code == finalCode, cancellationToken);
            if (codeConflict)
            {
                return Result<BibleEntryDto>.Failure(
                    $"Mã định danh (Code) '{finalCode}' đã tồn tại trong Series Bible.", ResultErrorCodes.Conflict);
            }
        }
        else
        {
            var prefix = entry.Category switch
            {
                BibleEntryCategory.Character => "CHR",
                BibleEntryCategory.Location => "LOC",
                BibleEntryCategory.Prop => "PRP",
                BibleEntryCategory.Terminology => "TRM",
                BibleEntryCategory.StyleRule => "STY",
                BibleEntryCategory.PlotFact => "PLT",
                _ => "BIB"
            };

            var cleanNameChars = definition.Name.Where(char.IsAsciiLetterOrDigit).ToArray();
            var cleanName = cleanNameChars.Length > 0
                ? new string(cleanNameChars).ToUpperInvariant()
                : Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

            finalCode = $"{prefix}_{cleanName}";
            var exists = await dbContext.BibleEntries
                .AnyAsync(e => e.SeriesBibleId == bible.Id && e.Id != entry.Id && e.Code == finalCode, cancellationToken);

            if (exists)
            {
                finalCode = $"{prefix}_{cleanName}_{Guid.NewGuid().ToString("N")[..4].ToUpperInvariant()}";
            }
        }

        try
        {
            entry.PromoteFromStub(
                code: finalCode,
                name: definition.Name,
                description: definition.Description,
                subtitle: definition.Subtitle,
                detailsJson: definition.DetailsJson,
                referenceImageUrl: definition.ReferenceImageUrl,
                priority: definition.Priority,
                strictCheck: definition.StrictCheck);

            if (definition.IsCrowd.HasValue)
            {
                entry.SetCrowd(definition.IsCrowd.Value);
            }

            entry.UpdatedBy = definition.UserId.ToString();
        }
        catch (ArgumentException ex)
        {
            return Result<BibleEntryDto>.Failure(ex.Message, ResultErrorCodes.Validation);
        }

        var rev1 = entry.Revisions.OrderBy(r => r.VersionNumber).FirstOrDefault();
        var stageName = rev1?.StageName;
        var snapshotObj = new
        {
            entry.Id,
            entry.Code,
            entry.Name,
            entry.Subtitle,
            entry.Description,
            entry.DetailsJson,
            entry.ReferenceImageUrl,
            entry.Priority,
            entry.StrictCheck,
            StageName = stageName
        };
        var snapshotJson = JsonSerializer.Serialize(snapshotObj);
        var contentHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(snapshotJson)));

        if (rev1 != null)
        {
            rev1.OverwriteDraft(
                summary: $"Định nghĩa mục {entry.Name} (Version 1)",
                snapshotJson: snapshotJson,
                contentHash: contentHash,
                authorId: definition.UserId,
                stageName: stageName);
        }
        else
        {
            var newRev = entry.AddRevision(
                summary: $"Định nghĩa mục {entry.Name} (Version 1)",
                snapshotJson: snapshotJson,
                contentHash: contentHash,
                authorId: definition.UserId,
                isInitialVersion: true,
                stageName: stageName);
            dbContext.BibleEntryRevisions.Add(newRev);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return Result<BibleEntryDto>.Success(BibleEntryDto.From(entry));
    }
}
