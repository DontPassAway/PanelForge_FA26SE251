using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Chapters.Common;
using PanelForge.Application.Features.PreviewLinks.Models;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.PreviewLinks.Queries;

/// <summary>Danh sách link xem thử của Chapter kèm thống kê truy cập (BR-17) và số phản hồi. Không bao giờ trả token.</summary>
public sealed record GetPreviewLinksQuery(Guid ChapterId) : IRequest<Result<IReadOnlyList<PreviewLinkDto>>>;

public sealed class GetPreviewLinksQueryHandler : IRequestHandler<GetPreviewLinksQuery, Result<IReadOnlyList<PreviewLinkDto>>>
{
    private readonly IPanelForgeDbContext _db;

    public GetPreviewLinksQueryHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<IReadOnlyList<PreviewLinkDto>>> Handle(GetPreviewLinksQuery q, CancellationToken ct)
    {
        var links = await _db.PreviewLinks.AsNoTracking().Where(l => l.ChapterId == q.ChapterId)
            .OrderByDescending(l => l.CreatedAt).ToListAsync(ct);
        var ids = links.Select(l => (Guid?)l.Id).ToList();
        var logs = (await _db.PreviewLinkAccessLogs.AsNoTracking()
                .Where(a => ids.Contains(a.PreviewLinkId))
                .Select(a => new { a.PreviewLinkId, a.Allowed, a.AccessedAt }).ToListAsync(ct))
            .ToLookup(a => a.PreviewLinkId!.Value);
        var linkIds = links.Select(l => l.Id).ToList();
        var feedback = (await _db.PreviewFeedback.AsNoTracking().Where(f => linkIds.Contains(f.PreviewLinkId))
                .Select(f => f.PreviewLinkId).ToListAsync(ct))
            .GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());

        var pageCounts = await FinalArtwork.PageCountsAsync(_db, links.Select(l => (IReadOnlyCollection<Guid>)l.SnapshotArtworkIds).ToList(), ct);
        var now = DateTime.UtcNow;
        var items = links.Select((l, i) => new PreviewLinkDto(
            l.Id, l.ChapterId, l.RecipientEmail, l.DenyReason(now) ?? "Active", l.CreatedAt, l.ExpiresAt, l.RevokedAt,
            l.RevokeReason, l.ChapterVersion, pageCounts[i],
            logs[l.Id].Count(a => a.Allowed), logs[l.Id].Count(a => !a.Allowed),
            logs[l.Id].Where(a => a.Allowed).Select(a => (DateTime?)a.AccessedAt).Max(),
            feedback.GetValueOrDefault(l.Id))).ToList();
        return Result<IReadOnlyList<PreviewLinkDto>>.Success(items);
    }
}

/// <summary>Phản hồi từ link xem thử của Chapter (mới trước), lọc tuỳ chọn theo trạng thái.</summary>
public sealed record GetPreviewFeedbackQuery(Guid ChapterId, PreviewFeedbackStatus? Status = null)
    : IRequest<Result<IReadOnlyList<PreviewFeedbackDto>>>;

public sealed class GetPreviewFeedbackQueryHandler
    : IRequestHandler<GetPreviewFeedbackQuery, Result<IReadOnlyList<PreviewFeedbackDto>>>
{
    private readonly IPanelForgeDbContext _db;

    public GetPreviewFeedbackQueryHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<IReadOnlyList<PreviewFeedbackDto>>> Handle(GetPreviewFeedbackQuery q, CancellationToken ct)
    {
        var query = _db.PreviewFeedback.AsNoTracking().Where(f => f.ChapterId == q.ChapterId);
        if (q.Status is { } s) query = query.Where(f => f.Status == s);
        var items = (await query.OrderByDescending(f => f.CreatedAt).ToListAsync(ct)).Select(PreviewFeedbackDto.From).ToList();
        return Result<IReadOnlyList<PreviewFeedbackDto>>.Success(items);
    }
}
