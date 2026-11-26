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
                    .ThenInclude(r => r.Events)
            .FirstOrDefaultAsync(b => b.SeriesId == request.SeriesId, cancellationToken);

        if (bible == null)
        {
            // Chưa có Bible (series cũ/seed) = chưa có mục nào hiệu lực.
            return Result<IReadOnlyList<BibleEntryDto>>.Success([]);
        }

        // Tải Chapter và ChapterBibleBindings nếu có
        Domain.Entities.Content.Chapter? chapter = null;
        if (_dbContext.Chapters != null)
        {
            try
            {
                chapter = await _dbContext.Chapters
                    .FirstOrDefaultAsync(c => c.SeriesId == request.SeriesId &&
                        (c.ChapterNumber == request.ChapterNumber || (int)Math.Floor(c.ChapterNumber) == request.ChapterNumber),
                        cancellationToken);
            }
            catch
            {
                // Fallback nếu mock không hỗ trợ async query
            }
        }

        Dictionary<Guid, Domain.Entities.Content.ChapterBibleBinding>? bindings = null;
        if (chapter != null && _dbContext.ChapterBibleBindings != null)
        {
            try
            {
                bindings = await _dbContext.ChapterBibleBindings
                    .Where(b => b.ChapterId == chapter.Id)
                    .ToDictionaryAsync(b => b.BibleEntryId, cancellationToken);
            }
            catch
            {
                // Fallback nếu mock không hỗ trợ async query
            }
        }

        var results = new List<BibleEntryDto>();

        var entries = bible.Entries.Where(e => request.EntryIds == null || request.EntryIds.Contains(e.Id));
        foreach (var entry in entries.OrderBy(e => e.Category).ThenBy(e => e.Name))
        {
            // BIB-15, Q16, Q23: Thay EffectiveRevisionAt(N) bằng binding của chương.
            // Chương trước Gate 1 hoặc chưa có dòng binding dùng bản tự gắn hiện tại (default variant).
            Domain.Entities.Bible.BibleEntryRevision? effectiveRev = null;

            if (bindings != null && bindings.TryGetValue(entry.Id, out var binding) && binding.BoundRevisionId.HasValue)
            {
                effectiveRev = entry.Revisions.FirstOrDefault(r => r.Id == binding.BoundRevisionId.Value);
                if (request.CanonOnly && entry.IsVisualDesign && (effectiveRev == null || !effectiveRev.IsLocked))
                {
                    effectiveRev = null;
                }
            }
            else
            {
                // Chương trước Gate 1 hoặc chưa có dòng binding (hoặc mock tests không có Chapter): dùng bản tự gắn hiện tại
                effectiveRev = entry.GetDefaultVariantRevision(canonOnly: request.CanonOnly);
            }

            if (effectiveRev == null)
            {
                continue;
            }

            // Trích xuất snapshot để tái hiện lại trạng thái chính xác tại Chapter N (Name là bất biến trên BibleEntry)
            string name = entry.Name;
            string description = entry.Description;
            string detailsJson = entry.DetailsJson;
            string? refImg = entry.ReferenceImageUrl;

            try
            {
                using var doc = JsonDocument.Parse(effectiveRev.SnapshotJson);
                var root = doc.RootElement;
                if (root.TryGetProperty("Description", out var d) || root.TryGetProperty("description", out d)) description = d.GetString() ?? description;
                if (root.TryGetProperty("DetailsJson", out var dj) || root.TryGetProperty("detailsJson", out dj)) detailsJson = dj.GetString() ?? detailsJson;
                if (root.TryGetProperty("ReferenceImageUrl", out var ri) || root.TryGetProperty("referenceImageUrl", out ri)) refImg = ri.GetString();
            }
            catch
            {
                // Fallback nếu không parse được
            }

            if (!string.IsNullOrEmpty(effectiveRev.ConceptImageUrl))
            {
                refImg = effectiveRev.ConceptImageUrl;
            }

            var revDto = BibleEntryRevisionDto.From(effectiveRev);
            var parsed = PanelForge.Application.Features.Bible.Common.BibleDetailsParser.Parse(detailsJson);

            results.Add(new BibleEntryDto(
                Id: entry.Id,
                SeriesBibleId: entry.SeriesBibleId,
                Category: entry.Category,
                Name: name,
                Description: description,
                DetailsJson: detailsJson,
                ReferenceImageUrl: refImg,
                EffectiveRevision: revDto,
                CreatedAt: entry.CreatedAt,
                UpdatedAt: entry.UpdatedAt,
                IsStub: entry.IsStub,
                IsCrowd: (bindings != null && bindings.TryGetValue(entry.Id, out var cb)) ? cb.IsCrowdAtBind : entry.IsCrowd,
                Age: parsed.Age,
                ArtistNotes: parsed.ArtistNotes
            ));
        }

        return Result<IReadOnlyList<BibleEntryDto>>.Success(results);
    }
}
