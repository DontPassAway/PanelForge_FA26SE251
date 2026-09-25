using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Bible.Models;
using PanelForge.Application.Interfaces.Persistence;

namespace PanelForge.Application.Features.Bible.Queries.GetBibleEntriesAtChapter;

public sealed record GetBibleEntriesAtChapterQuery(Guid SeriesId, int ChapterNumber) : IRequest<Result<IReadOnlyList<BibleEntryDto>>>;

public sealed class GetBibleEntriesAtChapterQueryHandler : IRequestHandler<GetBibleEntriesAtChapterQuery, Result<IReadOnlyList<BibleEntryDto>>>
{
    private readonly IPanelForgeDbContext _dbContext;

    public GetBibleEntriesAtChapterQueryHandler(IPanelForgeDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<IReadOnlyList<BibleEntryDto>>> Handle(GetBibleEntriesAtChapterQuery request, CancellationToken cancellationToken)
    {
        var bible = await _dbContext.SeriesBibles
            .Include(b => b.Entries)
                .ThenInclude(e => e.Revisions)
            .FirstOrDefaultAsync(b => b.SeriesId == request.SeriesId, cancellationToken);

        if (bible == null)
        {
            return Result<IReadOnlyList<BibleEntryDto>>.Failure($"SeriesBible cho Series {request.SeriesId} không tồn tại.");
        }

        var results = new List<BibleEntryDto>();

        foreach (var entry in bible.Entries.Where(e => e.IsActive).OrderBy(e => e.Category).ThenBy(e => e.Name))
        {
            // Lấy revision có EffectiveFromChapterNumber <= ChapterNumber của request (BR-15)
            // Lấy revision có EffectiveFromChapterNumber lớn nhất, và VersionNumber lớn nhất
            var effectiveRev = entry.Revisions
                .Where(r => r.EffectiveFromChapterNumber <= request.ChapterNumber)
                .OrderByDescending(r => r.EffectiveFromChapterNumber)
                .ThenByDescending(r => r.VersionNumber)
                .FirstOrDefault();

            // Nếu mục này chỉ xuất hiện từ chương sau (EffectiveFrom > ChapterNumber) và không có revision nào trước đó thì bỏ qua tại chapter này
            if (effectiveRev == null)
            {
                continue;
            }

            // Trích xuất snapshot để tái hiện lại trạng thái chính xác tại Chapter N
            string name = entry.Name;
            string description = entry.Description;
            string? subtitle = entry.Subtitle;
            string detailsJson = entry.DetailsJson;
            string? refImg = entry.ReferenceImageUrl;

            try
            {
                using var doc = JsonDocument.Parse(effectiveRev.SnapshotJson);
                var root = doc.RootElement;
                if (root.TryGetProperty("Name", out var n) || root.TryGetProperty("name", out n)) name = n.GetString() ?? name;
                if (root.TryGetProperty("Description", out var d) || root.TryGetProperty("description", out d)) description = d.GetString() ?? description;
                if (root.TryGetProperty("Subtitle", out var s) || root.TryGetProperty("subtitle", out s)) subtitle = s.GetString();
                if (root.TryGetProperty("DetailsJson", out var dj) || root.TryGetProperty("detailsJson", out dj)) detailsJson = dj.GetString() ?? detailsJson;
                if (root.TryGetProperty("ReferenceImageUrl", out var ri) || root.TryGetProperty("referenceImageUrl", out ri)) refImg = ri.GetString();
            }
            catch
            {
                // Fallback nếu không parse được
            }

            var revDto = new BibleEntryRevisionDto(
                Id: effectiveRev.Id,
                BibleEntryId: effectiveRev.BibleEntryId,
                VersionNumber: effectiveRev.VersionNumber,
                Summary: effectiveRev.Summary,
                SnapshotJson: effectiveRev.SnapshotJson,
                ContentHash: effectiveRev.ContentHash,
                AuthorId: effectiveRev.AuthorId,
                AssociatedChapterId: effectiveRev.AssociatedChapterId,
                EffectiveFromChapterNumber: effectiveRev.EffectiveFromChapterNumber,
                IsInitialVersion: effectiveRev.IsInitialVersion,
                CreatedAt: effectiveRev.CreatedAt
            );

            results.Add(new BibleEntryDto(
                Id: entry.Id,
                SeriesBibleId: entry.SeriesBibleId,
                Category: entry.Category,
                Code: entry.Code,
                Name: name,
                Subtitle: subtitle,
                Description: description,
                DetailsJson: detailsJson,
                ReferenceImageUrl: refImg,
                Priority: entry.Priority,
                StrictCheck: entry.StrictCheck,
                IsActive: entry.IsActive,
                EffectiveRevision: revDto,
                CreatedAt: entry.CreatedAt,
                UpdatedAt: entry.UpdatedAt
            ));
        }

        return Result<IReadOnlyList<BibleEntryDto>>.Success(results);
    }
}
