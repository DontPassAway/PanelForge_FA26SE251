using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Bible.Models;
using PanelForge.Domain.Common;

namespace PanelForge.Application.Features.Bible.Commands.UpdateBibleEntryAliases;

/// <summary>
/// Writer cập nhật danh sách bí danh (aliases) của mục Series Bible phục vụ auto-link kịch bản (BIB-01).
/// </summary>
public sealed record UpdateBibleEntryAliasesCommand(
    Guid SeriesId,
    Guid BibleEntryId,
    Guid UserId,
    IReadOnlyList<string> Aliases
) : IRequest<Result<BibleEntryDto>>;

public sealed class UpdateBibleEntryAliasesCommandHandler
    : IRequestHandler<UpdateBibleEntryAliasesCommand, Result<BibleEntryDto>>
{
    private readonly IPanelForgeDbContext _dbContext;

    public UpdateBibleEntryAliasesCommandHandler(IPanelForgeDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<BibleEntryDto>> Handle(UpdateBibleEntryAliasesCommand cmd, CancellationToken ct)
    {
        var bibleId = await _dbContext.SeriesBibles
            .Where(b => b.SeriesId == cmd.SeriesId)
            .Select(b => (Guid?)b.Id)
            .FirstOrDefaultAsync(ct);

        if (!bibleId.HasValue)
            return Result<BibleEntryDto>.Failure("Series Bible không tồn tại.", ResultErrorCodes.NotFound);

        var entry = await _dbContext.BibleEntries
            .Include(e => e.Revisions).ThenInclude(r => r.Events)
            .FirstOrDefaultAsync(e => e.Id == cmd.BibleEntryId && e.SeriesBibleId == bibleId.Value, ct);

        if (entry is null)
            return Result<BibleEntryDto>.Failure($"Mục BibleEntry {cmd.BibleEntryId} không tồn tại.", ResultErrorCodes.NotFound);

        var candidateNorms = (cmd.Aliases ?? [])
            .Select(BibleNameNormalizer.Normalize)
            .Where(n => !string.IsNullOrEmpty(n))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (candidateNorms.Count > 0)
        {
            var siblings = await _dbContext.BibleEntries
                .AsNoTracking()
                .Where(e => e.SeriesBibleId == bibleId.Value && e.Id != entry.Id)
                .Select(e => new { e.Id, e.Name, e.NormalizedName, e.Aliases })
                .ToListAsync(ct);

            foreach (var sib in siblings)
            {
                if (candidateNorms.Contains(sib.NormalizedName))
                {
                    return Result<BibleEntryDto>.Failure(
                        $"Bí danh trùng với tên mục '{sib.Name}' trong Series Bible.",
                        ResultErrorCodes.Conflict);
                }

                var collidingAlias = sib.Aliases.FirstOrDefault(a =>
                    candidateNorms.Contains(BibleNameNormalizer.Normalize(a)));
                if (collidingAlias != null)
                {
                    return Result<BibleEntryDto>.Failure(
                        $"Bí danh '{collidingAlias}' đã được dùng cho mục '{sib.Name}' trong Series Bible.",
                        ResultErrorCodes.Conflict);
                }
            }
        }

        try
        {
            entry.SetAliases(cmd.Aliases);
        }
        catch (ArgumentException ex)
        {
            return Result<BibleEntryDto>.Failure(ex.Message, ResultErrorCodes.Validation);
        }

        entry.UpdatedBy = cmd.UserId.ToString();
        await _dbContext.SaveChangesAsync(ct);

        return Result<BibleEntryDto>.Success(BibleEntryDto.From(entry));
    }
}
