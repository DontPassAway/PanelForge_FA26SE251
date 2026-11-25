using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Chapters.Common;
using PanelForge.Application.Features.Pages.Models;
using PanelForge.Application.Features.Pages.Queries.GetKeepOutZones;
using PanelForge.Domain.Entities.Content;

namespace PanelForge.Application.Features.Pages.Commands.ReplaceKeepOutZones;

/// <summary>Ghi đè toàn bộ vùng cấm chữ của trang (optimistic concurrency qua ExpectedVersion, BR-11).</summary>
public sealed record ReplaceKeepOutZonesCommand(Guid PageId, IReadOnlyList<KeepOutZoneDto> Zones, int? ExpectedVersion, Guid UserId)
    : IRequest<Result<PageKeepOutZonesDto>>;

public sealed class ReplaceKeepOutZonesCommandHandler : IRequestHandler<ReplaceKeepOutZonesCommand, Result<PageKeepOutZonesDto>>
{
    public const int MaxZones = 50;
    public const int MaxLabelLength = 100;
    private readonly IPanelForgeDbContext _db;

    public ReplaceKeepOutZonesCommandHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<PageKeepOutZonesDto>> Handle(ReplaceKeepOutZonesCommand c, CancellationToken ct)
    {
        var page = await _db.Pages.AsNoTracking().Where(p => p.Id == c.PageId)
            .Select(p => new { p.Id, p.ChapterId }).FirstOrDefaultAsync(ct);
        if (page is null)
            return Result<PageKeepOutZonesDto>.Failure($"Page {c.PageId} không tồn tại.", ResultErrorCodes.NotFound);
        if (await ChapterEditGuard.ReadOnlyReasonAsync(_db, page.ChapterId, ct) is { } readOnly)
            return Result<PageKeepOutZonesDto>.Failure(readOnly, ResultErrorCodes.ChapterReadOnly);

        var zones = c.Zones ?? [];
        if (Validate(zones) is { } error)
            return Result<PageKeepOutZonesDto>.Failure(error);

        var panelIds = zones.Where(z => z.PanelId != null).Select(z => z.PanelId!.Value).Distinct().ToList();
        if (panelIds.Count > 0)
        {
            var onPage = await _db.Panels.AsNoTracking().CountAsync(p => p.PageId == c.PageId && panelIds.Contains(p.Id), ct);
            if (onPage != panelIds.Count)
                return Result<PageKeepOutZonesDto>.Failure("Có panelId không thuộc trang này.");
        }

        var row = await _db.PageKeepOutZones.FirstOrDefaultAsync(z => z.PageId == c.PageId, ct);
        if (c.ExpectedVersion is { } expected && expected != (row?.Version ?? 0))
            return Result<PageKeepOutZonesDto>.Failure(
                "Vùng cấm chữ đã được người khác cập nhật. Tải lại rồi thử lại.", ResultErrorCodes.Conflict);

        if (row is null)
        {
            row = PageKeepOutZones.Create(c.PageId);
            _db.PageKeepOutZones.Add(row);
        }
        var cleaned = zones.Select(z => z with { Label = string.IsNullOrWhiteSpace(z.Label) ? null : z.Label.Trim() }).ToList();
        row.Replace(JsonSerializer.Serialize(cleaned, GetKeepOutZonesQueryHandler.Json), c.UserId.ToString());
        await _db.SaveChangesAsync(ct);

        return Result<PageKeepOutZonesDto>.Success(new PageKeepOutZonesDto(c.PageId, cleaned, row.Version, row.UpdatedAt));
    }

    internal static string? Validate(IReadOnlyList<KeepOutZoneDto> zones)
    {
        if (zones.Count > MaxZones) return $"Tối đa {MaxZones} vùng cấm mỗi trang.";
        foreach (var z in zones)
        {
            if (!InUnit(z.X) || !InUnit(z.Y) || !(z.Width > 0) || !(z.Height > 0)
                || z.X + z.Width > 1 + 1e-6 || z.Y + z.Height > 1 + 1e-6)
                return "Toạ độ vùng cấm phải chuẩn hoá trong 0..1 và nằm trong trang.";
            if (z.Label?.Length > MaxLabelLength) return $"Nhãn vùng cấm tối đa {MaxLabelLength} ký tự.";
        }
        return null;
    }

    private static bool InUnit(double v) => v >= 0 && v <= 1;
}
