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
    int EffectiveFromChapterNumber = 1
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
        var entry = await _dbContext.BibleEntries
            .Include(e => e.Revisions)
            .FirstOrDefaultAsync(e => e.Id == command.BibleEntryId, cancellationToken);

        if (entry == null)
        {
            return Result<BibleEntryRevisionDto>.Failure($"Mục BibleEntry {command.BibleEntryId} không tồn tại.");
        }

        // Cập nhật current state của entry để phản ánh thông tin mới nhất
        try
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
            entry.UpdatedBy = command.UserId.ToString();
        }
        catch (ArgumentException ex)
        {
            return Result<BibleEntryRevisionDto>.Failure(ex.Message);
        }

        // Tạo Snapshot lưu vào bản ghi revision mới (Append-only BR-01, BR-15)
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
            EffectiveFromChapterNumber = command.EffectiveFromChapterNumber
        };
        var snapshotJson = JsonSerializer.Serialize(snapshotObj);
        var contentHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(snapshotJson)));

        var revision = entry.AddRevision(
            summary: command.Summary,
            snapshotJson: snapshotJson,
            contentHash: contentHash,
            authorId: command.UserId,
            associatedChapterId: null,
            effectiveFromChapterNumber: Math.Max(1, command.EffectiveFromChapterNumber),
            isInitialVersion: false
        );

        _dbContext.BibleEntryRevisions.Add(revision);
        await _dbContext.SaveChangesAsync(cancellationToken);

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
}
