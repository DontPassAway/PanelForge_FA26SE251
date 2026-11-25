using PanelForge.Domain.Common;
using PanelForge.Domain.Enums;

namespace PanelForge.Domain.Entities.Publishing;

/// <summary>
/// UC-12 / UC-14 (CF5 bước 8–9): một lần xuất chương theo ExportPreset, chạy nền. Chụp danh sách artwork theo thứ tự trang
/// lúc tạo (như PreviewLink) nên file luôn là bản đã duyệt dù sau đó chương mở lại. Thử lại = tạo job mới (BR-01);
/// file kết quả lưu trong kho asset, chỉ tải qua API có kiểm quyền (BR-16).
/// </summary>
public class ExportJob : BaseEntity
{
    public Guid ChapterId { get; private set; }
    public Guid WorkspaceId { get; private set; }
    public Guid ExportPresetId { get; private set; }
    /// <summary>Mã preset lúc tạo (CBZ, PDF, PAGE_IMAGES, WEBTOON, OPEN_JSON); preset đổi sau đó không ảnh hưởng job.</summary>
    public string Format { get; private set; } = default!;
    public ExportJobStatus Status { get; private set; }
    public Guid RequestedByUserId { get; private set; }
    public long ChapterVersion { get; private set; }
    /// <summary>AssetReference artwork cuối của từng trang, theo thứ tự trang.</summary>
    public List<Guid> SnapshotArtworkIds { get; private set; } = [];

    public string? ResultStorageKey { get; private set; }
    public string? ResultFileName { get; private set; }
    public string? ResultContentType { get; private set; }
    public long? ResultSizeBytes { get; private set; }
    public string? ResultSha256 { get; private set; }
    public string? Error { get; private set; }
    public DateTime? StartedAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }
    public int DownloadCount { get; private set; }
    public DateTime? LastDownloadedAt { get; private set; }

    private ExportJob() { }

    public static ExportJob Create(
        Guid chapterId, Guid workspaceId, Guid exportPresetId, string format, Guid requestedByUserId,
        long chapterVersion, IEnumerable<Guid> snapshotArtworkIds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(format);
        var snapshot = snapshotArtworkIds.ToList();
        if (snapshot.Count == 0) throw new ArgumentException("Bản xuất phải có ít nhất một trang.", nameof(snapshotArtworkIds));

        return new ExportJob
        {
            ChapterId = chapterId,
            WorkspaceId = workspaceId,
            ExportPresetId = exportPresetId,
            Format = format.Trim().ToUpperInvariant(),
            Status = ExportJobStatus.Queued,
            RequestedByUserId = requestedByUserId,
            ChapterVersion = chapterVersion,
            SnapshotArtworkIds = snapshot,
            CreatedAt = DateTime.UtcNow
        };
    }

    /// <summary>Queued → Processing (worker nhận job).</summary>
    public void Start()
    {
        Require(ExportJobStatus.Queued, "bắt đầu");
        Status = ExportJobStatus.Processing;
        StartedAt = UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Processing → Completed, lưu thông tin file (SHA-256 tính trên đúng bytes đã lưu, BR-04).</summary>
    public void Complete(string storageKey, string fileName, string contentType, long sizeBytes, string sha256)
    {
        Require(ExportJobStatus.Processing, "hoàn tất");
        ResultStorageKey = storageKey;
        ResultFileName = fileName;
        ResultContentType = contentType;
        ResultSizeBytes = sizeBytes;
        ResultSha256 = sha256;
        Status = ExportJobStatus.Completed;
        CompletedAt = UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Processing → Failed kèm lỗi để hiện cho Producer (UC-12: cho thử lại bằng job mới).</summary>
    public void Fail(string error)
    {
        Require(ExportJobStatus.Processing, "đánh dấu lỗi");
        Error = string.IsNullOrWhiteSpace(error) ? "Lỗi không xác định." : error.Trim();
        Status = ExportJobStatus.Failed;
        CompletedAt = UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Queued → Cancelled. Job đang chạy không huỷ giữa chừng.</summary>
    public void Cancel()
    {
        Require(ExportJobStatus.Queued, "huỷ");
        Status = ExportJobStatus.Cancelled;
        CompletedAt = UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>BR-16: ghi lại mỗi lần tải file (audit theo SaveChanges).</summary>
    public void RecordDownload()
    {
        Require(ExportJobStatus.Completed, "tải");
        DownloadCount++;
        LastDownloadedAt = UpdatedAt = DateTime.UtcNow;
    }

    private void Require(ExportJobStatus expected, string action)
    {
        if (Status != expected)
            throw new InvalidOperationException($"Không thể {action} bản xuất đang ở trạng thái {Status}.");
    }
}
