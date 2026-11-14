using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Bible.Models;
using PanelForge.Domain.Entities.Bible;

namespace PanelForge.Application.Features.Bible.Queries.GetPanelBible;

/// <summary>
/// BIB-15, Q16, UC-07 bước 1, FR-Artist-04:
/// Lấy bản của từng thực thể trong Panel:
/// - panel_bible_variants nếu Panel có bản riêng (vd: hồi tưởng),
/// - ngược lại chapter_bible_bindings.bound_revision_id.
/// Lọc theo thực thể có dòng nhắc tới trong Panel (người nói hoặc bôi đen qua script_line_bible_links).
/// Trả Model Sheet của bản đó; ẩn các mục có is_crowd_at_bind.
/// </summary>
public sealed record GetPanelBibleQuery(Guid PanelId) : IRequest<Result<IReadOnlyList<BibleEntryDto>>>;

public sealed class GetPanelBibleQueryHandler : IRequestHandler<GetPanelBibleQuery, Result<IReadOnlyList<BibleEntryDto>>>
{
    private readonly IPanelForgeDbContext _dbContext;

    public GetPanelBibleQueryHandler(IPanelForgeDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<IReadOnlyList<BibleEntryDto>>> Handle(GetPanelBibleQuery request, CancellationToken cancellationToken)
    {
        var panelInfo = await _dbContext.Panels
            .Where(p => p.Id == request.PanelId)
            .Select(p => new
            {
                p.Id,
                ChapterId = p.Page.ChapterId,
                SeriesId = p.Page.Chapter.SeriesId
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (panelInfo is null)
            return Result<IReadOnlyList<BibleEntryDto>>.Failure($"Panel {request.PanelId} không tồn tại.", ResultErrorCodes.NotFound);

        // Thu thập các dòng trong Panel
        var lines = await _dbContext.ScriptLines
            .Where(l => l.PanelId == request.PanelId)
            .Select(l => new { l.Id, l.SpeakerCharacterId })
            .ToListAsync(cancellationToken);

        var lineIds = lines.Select(l => l.Id).ToList();

        // Thu thập liên kết tham chiếu (highlighted refs qua script_line_bible_links)
        var linkedEntryIds = await _dbContext.ScriptLineBibleLinks
            .Where(k => lineIds.Contains(k.ScriptLineId) && !k.IsDeleted)
            .Select(k => k.BibleEntryId)
            .ToListAsync(cancellationToken);

        var mentionedEntryIds = lines
            .Where(l => l.SpeakerCharacterId.HasValue)
            .Select(l => l.SpeakerCharacterId!.Value)
            .Concat(linkedEntryIds)
            .Distinct()
            .ToList();

        if (mentionedEntryIds.Count == 0)
            return Result<IReadOnlyList<BibleEntryDto>>.Success([]);

        // Tải các biến thể riêng theo Panel (panel_bible_variants)
        var panelVariants = await _dbContext.PanelBibleVariants
            .Where(v => v.PanelId == request.PanelId && mentionedEntryIds.Contains(v.BibleEntryId))
            .ToDictionaryAsync(v => v.BibleEntryId, cancellationToken);

        // Tải binding theo Chapter (chapter_bible_bindings)
        var chapterBindings = await _dbContext.ChapterBibleBindings
            .Where(b => b.ChapterId == panelInfo.ChapterId && mentionedEntryIds.Contains(b.BibleEntryId))
            .ToDictionaryAsync(b => b.BibleEntryId, cancellationToken);

        // Tải các BibleEntry cùng Revisions và Events
        var entries = await _dbContext.BibleEntries
            .Where(e => mentionedEntryIds.Contains(e.Id) && e.IsActive)
            .Include(e => e.Revisions).ThenInclude(r => r.Events)
            .ToListAsync(cancellationToken);

        var results = new List<BibleEntryDto>();

        foreach (var entry in entries.OrderBy(e => e.Category).ThenBy(e => e.Name))
        {
            // Ẩn các mục có is_crowd_at_bind (hoặc IsCrowd nếu chưa có binding)
            if (chapterBindings.TryGetValue(entry.Id, out var binding) && binding.IsCrowdAtBind)
                continue;
            if (!chapterBindings.ContainsKey(entry.Id) && entry.IsCrowd)
                continue;

            // Lấy bản từ panel_bible_variants nếu Panel có bản riêng, ngược lại chapter_bible_bindings.bound_revision_id
            BibleEntryRevision? revision = null;
            if (panelVariants.TryGetValue(entry.Id, out var pv))
            {
                revision = entry.Revisions.FirstOrDefault(r => r.Id == pv.RevisionId);
            }
            else if (binding?.BoundRevisionId != null)
            {
                revision = entry.Revisions.FirstOrDefault(r => r.Id == binding.BoundRevisionId.Value);
            }
            else
            {
                // Fallback biến thể mặc định
                revision = entry.GetDefaultVariantRevision();
            }

            if (revision == null)
                continue;

            // Trích xuất snapshot để có dữ liệu chính xác
            string name = entry.Name;
            string description = entry.Description;
            string? subtitle = entry.Subtitle;
            string detailsJson = entry.DetailsJson;
            string? refImg = entry.ReferenceImageUrl;

            try
            {
                using var doc = JsonDocument.Parse(revision.SnapshotJson);
                var root = doc.RootElement;
                if (root.TryGetProperty("Name", out var n) || root.TryGetProperty("name", out n)) name = n.GetString() ?? name;
                if (root.TryGetProperty("Description", out var d) || root.TryGetProperty("description", out d)) description = d.GetString() ?? description;
                if (root.TryGetProperty("Subtitle", out var s) || root.TryGetProperty("subtitle", out s)) subtitle = s.GetString();
                if (root.TryGetProperty("DetailsJson", out var dj) || root.TryGetProperty("detailsJson", out dj)) detailsJson = dj.GetString() ?? detailsJson;
                if (root.TryGetProperty("ReferenceImageUrl", out var ri) || root.TryGetProperty("referenceImageUrl", out ri)) refImg = ri.GetString();
            }
            catch
            {
                // Fallback nếu không parse được json snapshot
            }

            // Model Sheet: ConceptImageUrl của phiên bản
            if (!string.IsNullOrEmpty(revision.ConceptImageUrl))
            {
                refImg = revision.ConceptImageUrl;
            }

            var revDto = BibleEntryRevisionDto.From(revision);

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
                UpdatedAt: entry.UpdatedAt,
                IsStub: entry.IsStub,
                Aliases: entry.Aliases,
                IsCrowd: binding?.IsCrowdAtBind ?? entry.IsCrowd
            ));
        }

        return Result<IReadOnlyList<BibleEntryDto>>.Success(results);
    }
}
