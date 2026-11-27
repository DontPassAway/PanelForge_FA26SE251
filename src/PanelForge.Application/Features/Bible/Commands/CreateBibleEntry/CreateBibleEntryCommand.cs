using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Bible.Commands.DefineBibleEntry;
using PanelForge.Application.Features.Bible.Common;
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
    string Name,
    string Description,
    string? DetailsJson = null,
    string? ReferenceImageUrl = null,
    string? StageName = null,
    bool IsCrowd = false,
    string? Age = null,
    string? ArtistNotes = null,
    string? Code = null,
    string? Subtitle = null,
    BiblePriority Priority = BiblePriority.Standard,
    bool StrictCheck = true,
    IReadOnlyList<string>? Aliases = null
) : IRequest<Result<BibleEntryDto>>
{
    public CreateBibleEntryCommand(
        Guid seriesId,
        Guid userId,
        BibleEntryCategory category,
        string code,
        string name,
        string description)
        : this(seriesId, userId, category, name, description, Code: code)
    {
    }
}

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
            var workspaceId = await _dbContext.Series.AsNoTracking()
                .Where(s => s.Id == command.SeriesId).Select(s => (Guid?)s.WorkspaceId).FirstOrDefaultAsync(cancellationToken);
            if (workspaceId == null)
                return Result<BibleEntryDto>.Failure($"Series {command.SeriesId} không tồn tại.", ResultErrorCodes.NotFound);
            bible = SeriesBible.Create(command.SeriesId, workspaceId.Value, "Series Bible", "Series Bible mặc định");
            _dbContext.SeriesBibles.Add(bible);
        }

        var normalizedPrimary = BibleNameNormalizer.Normalize(command.Name);
        var existingPrimary = await _dbContext.BibleEntries
            .Include(e => e.Revisions)
            .FirstOrDefaultAsync(e => e.SeriesBibleId == bible.Id && e.NormalizedName == normalizedPrimary, cancellationToken);
        if (existingPrimary != null)
        {
            if (existingPrimary.IsStub)
            {
                return await BibleEntryStubDefinition.DefineAsync(
                    _dbContext,
                    bible,
                    existingPrimary,
                    new BibleEntryDefinition(
                        command.UserId,
                        command.Name,
                        command.Description,
                        command.DetailsJson,
                        command.ReferenceImageUrl,
                        command.IsCrowd,
                        command.Age,
                        command.ArtistNotes,
                        command.Subtitle),
                    cancellationToken);
            }

            return Result<BibleEntryDto>.Failure(
                $"Tên '{command.Name}' đã trùng với mục '{existingPrimary.Name}' trong Series Bible.",
                ResultErrorCodes.Conflict);
        }

        var detailsJson = (!string.IsNullOrWhiteSpace(command.Age) || !string.IsNullOrWhiteSpace(command.ArtistNotes))
            ? BibleDetailsParser.Serialize(command.Age, command.ArtistNotes)
            : (string.IsNullOrWhiteSpace(command.DetailsJson) ? "{}" : BibleDetailsParser.NormalizeJson(command.DetailsJson));

        var description = !string.IsNullOrWhiteSpace(command.Subtitle)
            ? $"({command.Subtitle.Trim()}) {command.Description.Trim()}"
            : command.Description.Trim();

        BibleEntry entry;
        try
        {
            entry = bible.AddEntry(
                category: command.Category,
                code: string.Empty,
                name: command.Name,
                description: description,
                detailsJson: detailsJson,
                referenceImageUrl: command.ReferenceImageUrl
            );
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

        var snapshotObj = new
        {
            entry.Id,
            entry.Name,
            entry.Description,
            entry.DetailsJson,
            entry.ReferenceImageUrl,
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
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result<BibleEntryDto>.Success(BibleEntryDto.From(entry));
    }
}
