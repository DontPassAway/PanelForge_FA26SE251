using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Bible.Common;
using PanelForge.Application.Features.Bible.Models;
using PanelForge.Domain.Common;
using PanelForge.Domain.Entities.Bible;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Bible.Commands.DefineBibleEntry;

internal sealed record BibleEntryDefinition(
    Guid UserId,
    string Name,
    string Description,
    string? DetailsJson = null,
    string? ReferenceImageUrl = null,
    bool? IsCrowd = null,
    string? Age = null,
    string? ArtistNotes = null,
    string? Subtitle = null);

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

        var detailsJson = (!string.IsNullOrWhiteSpace(definition.Age) || !string.IsNullOrWhiteSpace(definition.ArtistNotes))
            ? BibleDetailsParser.Serialize(definition.Age, definition.ArtistNotes)
            : (string.IsNullOrWhiteSpace(definition.DetailsJson) ? "{}" : BibleDetailsParser.NormalizeJson(definition.DetailsJson));

        var description = !string.IsNullOrWhiteSpace(definition.Description)
            ? definition.Description.Trim()
            : (definition.Subtitle?.Trim() ?? string.Empty);

        try
        {
            entry.PromoteFromStub(
                name: definition.Name,
                description: description,
                detailsJson: detailsJson,
                referenceImageUrl: definition.ReferenceImageUrl);

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
            entry.Name,
            entry.Description,
            entry.DetailsJson,
            entry.ReferenceImageUrl,
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
