using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Bible.Models;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Bible.Commands.DefineBibleEntry;

/// <summary>
/// BIB-13 (4A, Q19): Writer writes a definition for a stub entry.
/// The existing draft revision v1 is overwritten and the stub is promoted in place.
/// </summary>
public sealed record DefineBibleEntryCommand(
    Guid SeriesId,
    Guid BibleEntryId,
    Guid UserId,
    string Name,
    string Description,
    string? DetailsJson = null,
    string? ReferenceImageUrl = null,
    bool? IsCrowd = null,
    string? Age = null,
    string? ArtistNotes = null,
    string? Code = null,
    string? Subtitle = null,
    BiblePriority Priority = BiblePriority.Standard,
    bool StrictCheck = true,
    IReadOnlyList<string>? Aliases = null
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

        return await BibleEntryStubDefinition.DefineAsync(
            _dbContext,
            bible,
            entry,
            new BibleEntryDefinition(
                cmd.UserId,
                cmd.Name,
                cmd.Description,
                cmd.DetailsJson,
                cmd.ReferenceImageUrl,
                cmd.IsCrowd,
                cmd.Age,
                cmd.ArtistNotes,
                cmd.Subtitle),
            ct);
    }
}
