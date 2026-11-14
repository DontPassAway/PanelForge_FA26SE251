using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Bible.Models;
using PanelForge.Domain.Common;
using PanelForge.Domain.Entities.Bible;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Bible.Commands.DefineBibleEntry;

/// <summary>
/// BIB-13 (4A, Q19): Writer viết định nghĩa cho mục nháp (IsStub = true).
/// Mô tả bắt buộc; sinh mã chuẩn thay STUB_xxxx; PromoteFromStub();
/// ghi đè revision v1 Draft hiện có (không tạo v2); cập nhật bible_entry_names.
/// Sau đó mục vào hàng đợi Artist (Design Queue).
/// </summary>
public sealed record DefineBibleEntryCommand(
    Guid SeriesId,
    Guid BibleEntryId,
    Guid UserId,
    string Name,
    string Description,
    string? Subtitle = null,
    string? DetailsJson = null,
    string? ReferenceImageUrl = null,
    BiblePriority Priority = BiblePriority.Standard,
    bool StrictCheck = true,
    string? Code = null
) : IRequest<Result<BibleEntryDto>>;

public sealed class DefineBibleEntryCommandHandler : IRequestHandler<DefineBibleEntryCommand, Result<BibleEntryDto>>
{
    private readonly IPanelForgeDbContext _dbContext;

    public DefineBibleEntryCommandHandler(IPanelForgeDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<BibleEntryDto>> Handle(DefineBibleEntryCommand cmd, CancellationToken ct)
    {
        var bible = await _dbContext.SeriesBibles
            .FirstOrDefaultAsync(b => b.SeriesId == cmd.SeriesId, ct);

        if (bible == null)
        {
            return Result<BibleEntryDto>.Failure(
                $"Series {cmd.SeriesId} không tồn tại hoặc chưa có Series Bible.", ResultErrorCodes.NotFound);
        }

        var entry = await _dbContext.BibleEntries
            .Include(e => e.Revisions)
            .FirstOrDefaultAsync(e => e.Id == cmd.BibleEntryId && e.SeriesBibleId == bible.Id, ct);

        if (entry == null)
        {
            return Result<BibleEntryDto>.Failure(
                $"Mục BibleEntry {cmd.BibleEntryId} không tồn tại trong Series Bible này.", ResultErrorCodes.NotFound);
        }

        if (!entry.IsStub)
        {
            return Result<BibleEntryDto>.Failure(
                "Mục này đã được định nghĩa trước đó, không phải mục nháp (IsStub = false).", ResultErrorCodes.Conflict);
        }

        if (string.IsNullOrWhiteSpace(cmd.Description))
        {
            return Result<BibleEntryDto>.Failure(
                "Mô tả định nghĩa thực thể bắt buộc không được để trống.", ResultErrorCodes.Validation);
        }

        if (string.IsNullOrWhiteSpace(cmd.Name))
        {
            return Result<BibleEntryDto>.Failure(
                "Tên thực thể bắt buộc không được để trống.", ResultErrorCodes.Validation);
        }

        // Kiểm tra chống trùng tên chính và bí danh trong SeriesBible (BIB-09, 9A)
        var normalizedPrimary = BibleNameNormalizer.Normalize(cmd.Name);
        var existingPrimary = await _dbContext.BibleEntries
            .FirstOrDefaultAsync(e => e.SeriesBibleId == bible.Id && e.NormalizedName == normalizedPrimary && e.Id != entry.Id, ct);

        if (existingPrimary != null)
        {
            return Result<BibleEntryDto>.Failure(
                $"Tên '{cmd.Name}' đã trùng với mục '{existingPrimary.Name}' trong Series Bible.", ResultErrorCodes.Conflict);
        }

        // Sinh mã chuẩn thay thế STUB_xxxx
        string finalCode;
        if (!string.IsNullOrWhiteSpace(cmd.Code))
        {
            finalCode = cmd.Code.Trim().ToUpperInvariant();
            var codeConflict = await _dbContext.BibleEntries
                .AnyAsync(e => e.SeriesBibleId == bible.Id && e.Id != entry.Id && e.Code == finalCode, ct);
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

            var cleanNameChars = cmd.Name
                .Where(char.IsAsciiLetterOrDigit)
                .ToArray();

            var cleanName = cleanNameChars.Length > 0
                ? new string(cleanNameChars).ToUpperInvariant()
                : Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

            finalCode = $"{prefix}_{cleanName}";
            var exists = await _dbContext.BibleEntries
                .AnyAsync(e => e.SeriesBibleId == bible.Id && e.Id != entry.Id && e.Code == finalCode, ct);

            if (exists)
            {
                finalCode = $"{prefix}_{cleanName}_{Guid.NewGuid().ToString("N")[..4].ToUpperInvariant()}";
            }
        }

        try
        {
            entry.PromoteFromStub(
                code: finalCode,
                name: cmd.Name,
                description: cmd.Description,
                subtitle: cmd.Subtitle,
                detailsJson: cmd.DetailsJson,
                referenceImageUrl: cmd.ReferenceImageUrl,
                priority: cmd.Priority,
                strictCheck: cmd.StrictCheck);

            entry.UpdatedBy = cmd.UserId.ToString();
        }
        catch (ArgumentException ex)
        {
            return Result<BibleEntryDto>.Failure(ex.Message, ResultErrorCodes.Validation);
        }

        // Ghi đè revision v1 Draft hiện có (không tạo v2)
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
                authorId: cmd.UserId,
                stageName: stageName);
        }
        else
        {
            var newRev = entry.AddRevision(
                summary: $"Định nghĩa mục {entry.Name} (Version 1)",
                snapshotJson: snapshotJson,
                contentHash: contentHash,
                authorId: cmd.UserId,
                isInitialVersion: true,
                stageName: stageName);
            _dbContext.BibleEntryRevisions.Add(newRev);
        }

        await _dbContext.SaveChangesAsync(ct);

        return Result<BibleEntryDto>.Success(BibleEntryDto.From(entry));
    }
}
