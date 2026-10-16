using PanelForge.Application.Features.Consistency.Models;

namespace PanelForge.Application.Common.Interfaces.Services;

/// <summary>
/// Dịch vụ AI Vision kiểm tra tính nhất quán hình ảnh giữa Artwork và Character Sheet đã khóa (Bước A5, Task 3.1 & 3.2).
/// </summary>
public interface IImageConsistencyService
{
    /// <summary>
    /// Kiểm tra tính nhất quán hình ảnh cho một Panel cụ thể dựa trên Artwork mới nhất và Character Sheet khóa hiệu lực.
    /// </summary>
    Task<ImageConsistencyResultDto> CheckPanelConsistencyAsync(
        Guid panelId,
        string? revisionRef = null,
        decimal? threshold = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Chấm điểm Vision so sánh trực tiếp 2 ảnh và ghi nhận kết quả (AI Provenance & ConsistencyFinding).
    /// </summary>
    Task<ImageConsistencyResultDto> CompareImagesAsync(
        ImageConsistencyInput input,
        CancellationToken cancellationToken = default);
}
