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
    int EffectiveFromChapterNumber = 1
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
            return Result<BibleEntryDto>.Failure($"SeriesBible cho Series {command.SeriesId} không tồn tại.");
        }

        // Kiểm tra trùng Code trong cùng một SeriesBible
        var codeUpper = command.Code.Trim().ToUpperInvariant();
        if (bible.Entries.Any(e => e.Code == codeUpper))
        {
            return Result<BibleEntryDto>.Failure($"Mã định danh (Code) '{codeUpper}' đã tồn tại trong Series Bible này.");
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
            entry.CreatedBy = command.UserId.ToString();
        }
        catch (ArgumentException ex)
        {
            return Result<BibleEntryDto>.Failure(ex.Message);
        }

        var effectiveFrom = Math.Max(1, command.EffectiveFromChapterNumber);

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
            EffectiveFromChapterNumber = effectiveFrom
        };
        var snapshotJson = JsonSerializer.Serialize(snapshotObj);
        var contentHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(snapshotJson)));

        var revision = entry.AddRevision(
            summary: $"Khởi tạo mục {entry.Name} (Version 1)",
            snapshotJson: snapshotJson,
            contentHash: contentHash,
            authorId: command.UserId,
            associatedChapterId: null,
            effectiveFromChapterNumber: effectiveFrom,
            isInitialVersion: true
        );

        _dbContext.BibleEntries.Add(entry);
        _dbContext.BibleEntryRevisions.Add(revision);

        await _dbContext.SaveChangesAsync(cancellationToken);

        var revisionDto = new BibleEntryRevisionDto(
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
        );

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
            UpdatedAt: entry.UpdatedAt
        ));
    }
}
