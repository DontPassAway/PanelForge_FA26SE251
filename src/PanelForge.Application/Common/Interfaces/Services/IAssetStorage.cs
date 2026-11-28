namespace PanelForge.Application.Common.Interfaces.Services;

/// <summary>
/// Kho lưu nội dung file artwork. Là port vì đi qua ranh giới I/O và cần thay được (đĩa cục bộ hôm nay, object storage sau này).
/// Khoá do server sinh theo nội dung (<c>Asset.StorageKey</c>), không bao giờ lấy từ dữ liệu client.
/// </summary>
public interface IAssetStorage
{
    /// <summary>
    /// Lưu nội dung dưới khoá đã cho. Idempotent: khoá đã có nội dung thì giữ nguyên (nội dung gắn theo hash).
    /// <paramref name="info"/> chỉ để người xem kho dễ nhận ra file (content-type, nhãn); kho nào không hỗ trợ thì bỏ qua.
    /// </summary>
    Task SaveAsync(string key, byte[] content, AssetObjectInfo? info, CancellationToken cancellationToken = default);

    /// <summary>Mở luồng đọc; null nếu không có. Người gọi chịu trách nhiệm dispose.</summary>
    Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Liệt kê mọi file có khoá bắt đầu bằng <paramref name="prefix"/> ("" = cả kho). Chỉ đọc (OPS-22 đối soát dung lượng).</summary>
    IAsyncEnumerable<StoredObject> ListAsync(string prefix, CancellationToken cancellationToken = default);

    /// <summary>Xoá một file. Khoá không tồn tại thì bỏ qua. Chỉ dùng cho file mồ côi Admin đã xác nhận (OPS-22).</summary>
    Task DeleteAsync(string key, CancellationToken cancellationToken = default);
}

public sealed record StoredObject(string Key, long SizeBytes, DateTime LastModifiedUtc);

/// <summary>Thông tin mô tả đi kèm file trên kho (vd. metadata object R2). Không dùng cho nghiệp vụ – nguồn sự thật vẫn là DB.</summary>
public sealed record AssetObjectInfo(string ContentType, IReadOnlyDictionary<string, string> Metadata);
