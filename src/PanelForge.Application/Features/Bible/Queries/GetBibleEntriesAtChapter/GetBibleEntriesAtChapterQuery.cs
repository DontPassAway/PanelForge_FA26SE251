using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Bible.Models;
using PanelForge.Application.Common.Interfaces.Persistence;

namespace PanelForge.Application.Features.Bible.Queries.GetBibleEntriesAtChapter;

/// <param name="CanonOnly">Nhân vật chỉ lấy bản Producer đã khoá, đúng nguồn AI dùng (AiSnapshotBuilder).</param>
/// <param name="EntryIds">Chỉ lấy các mục này (vd. nhân vật trong một Panel); null = tất cả.</param>
public sealed record GetBibleEntriesAtChapterQuery(
    Guid SeriesId, int ChapterNumber, bool CanonOnly = false, IReadOnlyCollection<Guid>? EntryIds = null) : IRequest<Result<IReadOnlyList<BibleEntryDto>>>;

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

        var entries = bible.Entries.Where(e => e.IsActive && (request.EntryIds == null || request.EntryIds.Contains(e.Id)));
        foreach (var entry in entries.OrderBy(e => e.Category).ThenBy(e => e.Name))
        {
            // BR-15. Mặc định hiện cả nhân vật chưa khoá (DTO có IsLocked); CanonOnly = chỉ bản khoá, giống AI.
            var effectiveRev = entry.EffectiveRevisionAt(request.ChapterNumber, request.CanonOnly);

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
                CreatedAt: effectiveRev.CreatedAt,
                IsLocked: effectiveRev.IsLocked,
                LockedAt: effectiveRev.LockedAt,
                LockedById: effectiveRev.LockedById,
            StageName: effectiveRev.StageName,
            EffectiveToChapterNumber: effectiveRev.EffectiveToChapterNumber
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
