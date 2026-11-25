namespace PanelForge.Application.Features.Pages.Models;

/// <summary>Một vùng cấm chữ; toạ độ chuẩn hoá 0..1 theo kích thước trang.</summary>
public sealed record KeepOutZoneDto(double X, double Y, double Width, double Height, string? Label, Guid? PanelId);

/// <param name="Version">Gửi lại làm ExpectedVersion khi PUT (0 = chưa từng lưu).</param>
public sealed record PageKeepOutZonesDto(Guid PageId, IReadOnlyList<KeepOutZoneDto> Zones, int Version, DateTime? UpdatedAt);

public sealed record ReplaceKeepOutZonesRequest(IReadOnlyList<KeepOutZoneDto> Zones, int? ExpectedVersion);
