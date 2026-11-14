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
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Bible.Commands.CreateBibleEntry;

public sealed record CreateBibleEntryCommand(
    Guid SeriesId,
    Guid UserId,
    BibleEntryCategory Category,
    string Code,
    string Name,
    string Description,
    string? Subtitle = null,
    string? DetailsJson = null,
    string? ReferenceImageUrl = null,
    BiblePriority Priority = BiblePriority.Standard,
    bool StrictCheck = true,
    string? StageName = null,
    IReadOnlyList<string>? Aliases = null,
    bool IsCrowd = false
) : IRequest<Result<BibleEntryDto>>;

public sealed class CreateBibleEntryCommandHandler : IRequestHandler<CreateBibleEntryCommand, Result<BibleEntryDto>>
{
    private readonly IPanelForgeDbContext _dbContext;

    public CreateBibleEntryCommandHandler(IPanelForgeDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<BibleEntryDto>> Handle(CreateBibleEntryCommand command, CancellationToken cancellationToken)
    {
        var bible = await _dbContext.SeriesBibles
            .Include(b => b.Entries)
            .FirstOrDefaultAsync(b => b.SeriesId == command.SeriesId, cancellationToken);

        if (bible == null)
        {
            // Spec: mỗi Series có một Bible rỗng; series cũ/seed chưa có thì tạo ngay (giống LinkBibleEntries).
            var workspaceId = await _dbContext.Series.AsNoTracking()
                .Where(s => s.Id == command.SeriesId).Select(s => (Guid?)s.WorkspaceId).FirstOrDefaultAsync(cancellationToken);
            if (workspaceId == null)
                return Result<BibleEntryDto>.Failure($"Series {command.SeriesId} không tồn tại.", ResultErrorCodes.NotFound);
            bible = SeriesBible.Create(command.SeriesId, workspaceId.Value, "Series Bible", "Series Bible mặc định");
            _dbContext.SeriesBibles.Add(bible);
        }

        // Kiểm tra trùng Code trong cùng một SeriesBible
        var codeUpper = command.Code.Trim().ToUpperInvariant();
        if (bible.Entries.Any(e => e.Code == codeUpper))
        {
            return Result<BibleEntryDto>.Failure($"Mã định danh (Code) '{codeUpper}' đã tồn tại trong Series Bible này.");
        }

        // BIB-09, 9A: Kiểm tra trùng tên chính và bí danh trong Series Bible (409)
        var normalizedPrimary = BibleNameNormalizer.Normalize(command.Name);
        var existingPrimary = await _dbContext.BibleEntryNames
            .Include(n => n.BibleEntry)
            .FirstOrDefaultAsync(n => n.SeriesBibleId == bible.Id && n.NormalizedName == normalizedPrimary, cancellationToken);
        if (existingPrimary != null)
        {
            var conflictingName = existingPrimary.BibleEntry?.Name ?? existingPrimary.NormalizedName;
            return Result<BibleEntryDto>.Failure(
                $"Tên '{command.Name}' đã trùng với mục '{conflictingName}' trong Series Bible.",
                ResultErrorCodes.Conflict);
        }

        if (command.Aliases != null)
        {
            foreach (var alias in command.Aliases)
            {
                var norm = BibleNameNormalizer.Normalize(alias);
                if (string.IsNullOrWhiteSpace(norm)) continue;
                var existingAlias = await _dbContext.BibleEntryNames
                    .Include(n => n.BibleEntry)
                    .FirstOrDefaultAsync(n => n.SeriesBibleId == bible.Id && n.NormalizedName == norm, cancellationToken);
                if (existingAlias != null)
                {
                    var conflictingName = existingAlias.BibleEntry?.Name ?? existingAlias.NormalizedName;
                    return Result<BibleEntryDto>.Failure(
                        $"Bí danh '{alias}' đã trùng với mục '{conflictingName}' trong Series Bible.",
                        ResultErrorCodes.Conflict);
                }
            }
        }

        BibleEntry entry;
        try
        {
            entry = bible.AddEntry(
                category: command.Category,
                code: codeUpper,
                name: command.Name,
                description: command.Description,
                subtitle: command.Subtitle,
                detailsJson: command.DetailsJson,
                referenceImageUrl: command.ReferenceImageUrl,
                priority: command.Priority,
                strictCheck: command.StrictCheck
            );
            entry.SetAliases(command.Aliases);
            if (command.IsCrowd)
            {
                entry.SetCrowd(true);
            }
            entry.CreatedBy = command.UserId.ToString();
        }
        catch (ArgumentException ex)
        {
            return Result<BibleEntryDto>.Failure(ex.Message);
        }

        // Tự động sinh snapshot & revision v1 ban đầu (BR-18, CF1 Step 8)
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
            StageName = command.StageName?.Trim()
        };
        var snapshotJson = JsonSerializer.Serialize(snapshotObj);
        var contentHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(snapshotJson)));

        var revision = entry.AddRevision(
            summary: $"Khởi tạo mục {entry.Name} (Version 1)",
            snapshotJson: snapshotJson,
            contentHash: contentHash,
            authorId: command.UserId,
            associatedChapterId: null,
            isInitialVersion: true,
            stageName: command.StageName
        );

        _dbContext.BibleEntries.Add(entry);
        _dbContext.BibleEntryRevisions.Add(revision);

        // BIB-09, 9A: Đồng bộ vào bảng bible_entry_names trong cùng transaction
        _dbContext.BibleEntryNames.Add(BibleEntryName.Create(bible.Id, entry.Name, entry.Id, isPrimary: true));
        foreach (var alias in entry.Aliases)
        {
            _dbContext.BibleEntryNames.Add(BibleEntryName.Create(bible.Id, alias, entry.Id, isPrimary: false));
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        // V2.5 Stub Auto-Matching: Remap toàn bộ script_line_bible_links của Stub có cùng tên sang BibleEntry chính thức vừa tạo
        var cleanName = entry.Name.Trim();
        var matchingStubs = await _dbContext.BibleEntries
            .Where(e => e.SeriesBibleId == bible.Id && e.IsStub && e.Name.ToLower() == cleanName.ToLower() && e.Id != entry.Id)
            .ToListAsync(cancellationToken);

        if (matchingStubs.Count > 0)
        {
            var stubIds = matchingStubs.Select(s => s.Id).ToList();
            var linksToRemap = await _dbContext.ScriptLineBibleLinks
                .Where(l => stubIds.Contains(l.BibleEntryId))
                .ToListAsync(cancellationToken);

            foreach (var link in linksToRemap)
            {
                link.RemapTargetEntry(entry.Id);
            }

            foreach (var stub in matchingStubs)
            {
                stub.SetActiveStatus(false);
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        var revisionDto = BibleEntryRevisionDto.From(revision);

        return Result<BibleEntryDto>.Success(new BibleEntryDto(
            Id: entry.Id,
            SeriesBibleId: entry.SeriesBibleId,
            Category: entry.Category,
            Code: entry.Code,
            Name: entry.Name,
            Subtitle: entry.Subtitle,
            Description: entry.Description,
            DetailsJson: entry.DetailsJson,
            ReferenceImageUrl: entry.ReferenceImageUrl,
            Priority: entry.Priority,
            StrictCheck: entry.StrictCheck,
            IsActive: entry.IsActive,
            EffectiveRevision: revisionDto,
            CreatedAt: entry.CreatedAt,
            UpdatedAt: entry.UpdatedAt,
            IsStub: entry.IsStub,
            Aliases: entry.Aliases,
            IsCrowd: entry.IsCrowd
        ));
    }
}
