using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Models;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Catalog;

// BR-23 / BR-24: public catalog of Published Series, độc lập với workspace membership.
// Chỉ lộ thông tin đã công bố: Series có ít nhất một Chapter Published và danh sách các Chapter Published đó.
// Không bao giờ trả Series Bible, pipeline, thành viên hay Chapter chưa Published.

public sealed record PublicSeriesDto(
    Guid Id,
    string Title,
    string? Synopsis,
    string Genre,
    string Format,
    ReadingDirection ReadingDirection,
    string StudioName,
    int PublishedChapterCount,
    DateTime? LatestPublishedAt,
    string? CoverUrl = null,
    bool IsCompleted = false,
    DateTime? CreatedAt = null,
    int ViewCount = 0,
    int ViewsLast1Day = 0,
    int ViewsLast7Days = 0,
    int ViewsLast30Days = 0);

/// <param name="CoverUrl">Ảnh bìa chương công khai (không cần đăng nhập); null = chương chưa có bìa.</param>
public sealed record PublicChapterDto(
    Guid Id,
    decimal ChapterNumber,
    string? Title,
    DateTime? PublishedAt,
    string? CoverUrl = null,
    bool IsLocked = false,
    decimal PriceVnd = 0m,
    bool IsUnlocked = true,
    bool IsFree = true);

public sealed record PublicSeriesDetailDto(
    Guid Id,
    string Title,
    string? Synopsis,
    string Genre,
    string Format,
    ReadingDirection ReadingDirection,
    string StudioName,
    IReadOnlyList<PublicChapterDto> Chapters,
    string? CoverUrl = null,
    bool IsCompleted = false,
    DateTime? CreatedAt = null,
    int ViewCount = 0);

// ─── List ─────────────────────────────────────────────────────────────────────

/// <param name="Search">Tìm theo tên Series (không phân biệt hoa thường).</param>
/// <param name="Genre">Lọc chính xác theo thể loại.</param>
public sealed record GetPublicCatalogQuery(string? Search = null, string? Genre = null, int PageIndex = 1, int PageSize = 20)
    : IRequest<Result<PagedResult<PublicSeriesDto>>>;

public sealed class GetPublicCatalogQueryHandler : IRequestHandler<GetPublicCatalogQuery, Result<PagedResult<PublicSeriesDto>>>
{
    private readonly IPanelForgeDbContext _dbContext;
    public GetPublicCatalogQueryHandler(IPanelForgeDbContext dbContext) => _dbContext = dbContext;

    public async Task<Result<PagedResult<PublicSeriesDto>>> Handle(GetPublicCatalogQuery q, CancellationToken ct)
    {
        var pageIndex = Math.Max(1, q.PageIndex);
        var pageSize = Math.Clamp(q.PageSize, 1, 50);
        var now = DateTime.UtcNow; // chương lên lịch (PublishedAt tương lai) chưa lộ ra cho Reader

        var query = _dbContext.Series.AsNoTracking()
            .Where(s => s.Chapters.Any(c => c.Status == ChapterStatus.Published && c.PublishedAt <= now));

        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var search = q.Search.Trim().ToLower();
            query = query.Where(s => s.Title.ToLower().Contains(search));
        }

        if (!string.IsNullOrWhiteSpace(q.Genre))
        {
            var genre = q.Genre.Trim();
            query = query.Where(s => s.Genre == genre);
        }

        var total = await query.CountAsync(ct);
        var day = now.AddDays(-1);
        var week = now.AddDays(-7);
        var month = now.AddDays(-30);

        // Sắp xếp + phân trang trên entity TRƯỚC khi projection vào positional record:
        // EF Core không dịch được OrderBy theo member của record (LatestPublishedAt) sau khi đã Select.
        var rows = await query
            .OrderByDescending(s => s.Chapters.Where(c => c.Status == ChapterStatus.Published && c.PublishedAt <= now).Max(c => c.PublishedAt))
            .ThenBy(s => s.Title)
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .Select(s => new
            {
                Dto = new PublicSeriesDto(
                    s.Id,
                    s.Title,
                    s.Synopsis,
                    s.Genre,
                    s.Format,
                    s.ReadingDirection,
                    s.Workspace.Name,
                    s.Chapters.Count(c => c.Status == ChapterStatus.Published && c.PublishedAt <= now),
                    s.Chapters.Where(c => c.Status == ChapterStatus.Published && c.PublishedAt <= now).Max(c => c.PublishedAt),
                    null,
                    s.IsCompleted,
                    s.CreatedAt,
                    _dbContext.ChapterViews.Count(v => v.SeriesId == s.Id),
                    _dbContext.ChapterViews.Count(v => v.SeriesId == s.Id && v.ViewedAt >= day),
                    _dbContext.ChapterViews.Count(v => v.SeriesId == s.Id && v.ViewedAt >= week),
                    _dbContext.ChapterViews.Count(v => v.SeriesId == s.Id && v.ViewedAt >= month)),
                s.CoverImageSha256
            })
            .ToListAsync(ct);
        var items = rows
            .Select(r => r.Dto with { CoverUrl = GetPublicSeriesCoverQueryHandler.CoverUrl(r.Dto.Id, r.CoverImageSha256) })
            .ToList();

        return Result<PagedResult<PublicSeriesDto>>.Success(new PagedResult<PublicSeriesDto>(
            items, total, pageIndex, pageSize, (int)Math.Ceiling(total / (double)pageSize)));
    }
}

// ─── Detail ───────────────────────────────────────────────────────────────────

public sealed record GetPublicSeriesDetailQuery(Guid SeriesId) : IRequest<Result<PublicSeriesDetailDto>>;

public sealed class GetPublicSeriesDetailQueryHandler : IRequestHandler<GetPublicSeriesDetailQuery, Result<PublicSeriesDetailDto>>
{
    private readonly IPanelForgeDbContext _dbContext;
    private readonly ICurrentUserService _currentUser;

    public GetPublicSeriesDetailQueryHandler(IPanelForgeDbContext dbContext, ICurrentUserService currentUser)
    {
        _dbContext = dbContext;
        _currentUser = currentUser;
    }

    public async Task<Result<PublicSeriesDetailDto>> Handle(GetPublicSeriesDetailQuery q, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var series = await _dbContext.Series.AsNoTracking()
            .Where(s => s.Id == q.SeriesId)
            .Select(s => new
            {
                s.Id,
                s.Title,
                s.Synopsis,
                s.Genre,
                s.Format,
                s.ReadingDirection,
                s.WorkspaceId,
                StudioName = s.Workspace.Name,
                s.CoverImageSha256,
                s.IsCompleted,
                s.CreatedAt,
                Chapters = s.Chapters
                    .Where(c => c.Status == ChapterStatus.Published && c.PublishedAt <= now)
                    .OrderBy(c => c.ChapterNumber)
                    .Select(c => new
                    {
                        c.Id,
                        c.ChapterNumber,
                        c.Title,
                        c.PublishedAt,
                        c.CoverImageSha256,
                        c.IsLocked,
                        c.PriceVnd
                    })
                    .ToList()
            })
            .FirstOrDefaultAsync(ct);

        // Series chưa có chương Published coi như không tồn tại với public (không lộ Series đang sản xuất)
        if (series is null || series.Chapters.Count == 0)
            return Result<PublicSeriesDetailDto>.Failure("Không tìm thấy Series đã phát hành.", ResultErrorCodes.NotFound);

        // Kiểm tra quyền VIP hoặc các chương đã mua nếu có người dùng đăng nhập
        // Cùng quy tắc với API đọc chương (ChapterReadAccess): Publisher/Admin/thành viên Studio, VIP còn hạn, hoặc đã mua.
        bool canReadAll = false;
        var unlockedIds = new HashSet<Guid>();
        var currentUserId = _currentUser.UserId;
        if (currentUserId.HasValue)
        {
            canReadAll = await ChapterReadAccess.IsStaffAsync(_dbContext, currentUserId, series.WorkspaceId, ct)
                || await ChapterReadAccess.HasActiveVipAsync(_dbContext, currentUserId.Value, now, ct);

            var ids = await _dbContext.ChapterEntitlements
                .Where(e => e.UserId == currentUserId.Value && !e.IsDeleted)
                .Select(e => e.ChapterId)
                .ToListAsync(ct);
            unlockedIds = ids.ToHashSet();
        }

        var chapterDtos = series.Chapters.Select(c =>
        {
            bool isUnlocked = !c.IsLocked || canReadAll || unlockedIds.Contains(c.Id);
            return new PublicChapterDto(
                Id: c.Id,
                ChapterNumber: c.ChapterNumber,
                Title: c.Title,
                PublishedAt: c.PublishedAt,
                CoverUrl: GetPublicChapterCoverQueryHandler.CoverUrl(c.Id, c.CoverImageSha256),
                IsLocked: c.IsLocked,
                PriceVnd: c.PriceVnd,
                IsUnlocked: isUnlocked,
                IsFree: !c.IsLocked
            );
        }).ToList();

        return Result<PublicSeriesDetailDto>.Success(new PublicSeriesDetailDto(
            series.Id, series.Title, series.Synopsis, series.Genre, series.Format, series.ReadingDirection,
            series.StudioName, chapterDtos,
            GetPublicSeriesCoverQueryHandler.CoverUrl(series.Id, series.CoverImageSha256),
            series.IsCompleted,
            series.CreatedAt,
            await _dbContext.ChapterViews.CountAsync(v => v.SeriesId == series.Id, ct)));
    }
}
