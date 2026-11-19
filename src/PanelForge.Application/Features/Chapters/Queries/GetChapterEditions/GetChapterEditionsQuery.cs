using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Chapters.Models;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Chapters.Queries.GetChapterEditions;

public sealed record GetChapterEditionsQuery(Guid ChapterId) : IRequest<Result<IReadOnlyList<ChapterEditionDto>>>;

public sealed class GetChapterEditionsQueryHandler : IRequestHandler<GetChapterEditionsQuery, Result<IReadOnlyList<ChapterEditionDto>>>
{
    private readonly IPanelForgeDbContext _db;

    public GetChapterEditionsQueryHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<IReadOnlyList<ChapterEditionDto>>> Handle(GetChapterEditionsQuery q, CancellationToken ct)
    {
        var chapter = await _db.Chapters.AsNoTracking().FirstOrDefaultAsync(c => c.Id == q.ChapterId, ct);
        if (chapter is null)
            return Result<IReadOnlyList<ChapterEditionDto>>.Failure($"Chương {q.ChapterId} không tồn tại.", ResultErrorCodes.NotFound);

        // Đảm bảo các ấn bản theo ReleaseLanguages của Series tồn tại
        var series = await _db.Series.AsNoTracking().FirstOrDefaultAsync(s => s.Id == chapter.SeriesId, ct);
        var releaseLangs = series?.ReleaseLanguages ?? [ContentLanguages.Vietnamese, ContentLanguages.English];

        var existing = await _db.ChapterEditions
            .Include(e => e.TranslatorUser)
            .Where(e => e.ChapterId == q.ChapterId)
            .ToListAsync(ct);

        var existingLangs = existing.Select(e => e.LanguageCode.ToLowerInvariant()).ToHashSet();

        bool hasNew = false;
        foreach (var lang in releaseLangs)
        {
            if (!existingLangs.Contains(lang.ToLowerInvariant()))
            {
                var ed = ChapterEdition.Create(chapter.Id, lang, EditionStatus.Draft);
                _db.ChapterEditions.Add(ed);
                existing.Add(ed);
                hasNew = true;
            }
        }

        if (hasNew)
        {
            await _db.SaveChangesAsync(ct);
        }

        var approvedByIds = existing.Where(e => e.ApprovedById.HasValue).Select(e => e.ApprovedById!.Value).Distinct().ToList();
        var approverNames = approvedByIds.Count > 0
            ? await _db.Users.AsNoTracking()
                .Where(u => approvedByIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.FullName, ct)
            : new Dictionary<Guid, string>();

        var dtos = existing
            .OrderBy(e => e.LanguageCode == (series?.OriginalLanguage ?? ContentLanguages.Original) ? 0 : 1)
            .ThenBy(e => e.LanguageCode)
            .Select(e => ChapterEditionMapping.ToDto(
                e,
                e.TranslatorUser?.FullName,
                e.ApprovedById.HasValue ? approverNames.GetValueOrDefault(e.ApprovedById.Value) : null))
            .ToList();

        return Result<IReadOnlyList<ChapterEditionDto>>.Success(dtos);
    }
}
