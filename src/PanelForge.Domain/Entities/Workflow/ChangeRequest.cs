using System.Text.Json;
using PanelForge.Domain.Common;
using PanelForge.Domain.Enums;

namespace PanelForge.Domain.Entities.Workflow;

/// <summary>
/// Yêu cầu sửa do Editor tạo khi trả việc về (CF3 bước 7–9). Gắn Assignment, lần nộp bị từ chối, người chịu trách nhiệm,
/// hạn, và vùng tuỳ chọn (tọa độ chuẩn hóa 0–1 của ảnh gốc). Vòng đời: Open → Resolved → Verified;
/// Resolved → Open nếu chưa đạt; Open → Rejected khi chấp nhận phản đối.
/// </summary>
public class ChangeRequest : BaseEntity
{
    public Guid AssignmentId { get; private set; }
    public Guid ChapterId { get; private set; }
    public Guid? SubmissionId { get; private set; }
    public Guid ResponsibleUserId { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public string Description { get; private set; } = default!;
    /// <summary>JSON <c>{ "x", "y", "w", "h" }</c> trong khoảng 0–1; null = áp dụng cho cả item.</summary>
    public string? RegionJson { get; private set; }
    /// <summary>Phiên bản artwork (AssetReference) mà vùng khoanh thuộc về; null = không gắn ảnh cụ thể (CF3 bước 3).</summary>
    public Guid? AssetReferenceId { get; private set; }
    /// <summary>Panel mà vùng khoanh thuộc về (FE suy từ vùng giao lớn nhất); null = không xác định.</summary>
    public Guid? PanelId { get; private set; }
    public DateTime? DueDate { get; private set; }
    public ChangeRequestStatus Status { get; private set; }

    public string? ResolutionNote { get; private set; }
    public DateTime? ResolvedAt { get; private set; }
    /// <summary>Comment của Editor khi mở lại hoặc từ chối.</summary>
    public string? ReviewerComment { get; private set; }
    public Guid? ClosedByUserId { get; private set; }
    public DateTime? ClosedAt { get; private set; }

    private ChangeRequest() { }

    public static ChangeRequest Create(
        Guid assignmentId, Guid chapterId, Guid? submissionId, Guid responsibleUserId, Guid createdByUserId,
        string description, string? regionJson = null, DateTime? dueDate = null, Guid? assetReferenceId = null, Guid? panelId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        if (ValidateRegion(regionJson) is { } regionError) throw new ArgumentException(regionError, nameof(regionJson));

        return new ChangeRequest
        {
            AssignmentId = assignmentId,
            ChapterId = chapterId,
            SubmissionId = submissionId,
            ResponsibleUserId = responsibleUserId,
            CreatedByUserId = createdByUserId,
            Description = description.Trim(),
            RegionJson = string.IsNullOrWhiteSpace(regionJson) ? null : regionJson.Trim(),
            AssetReferenceId = assetReferenceId,
            PanelId = panelId,
            DueDate = dueDate,
            Status = ChangeRequestStatus.Open,
            CreatedAt = DateTime.UtcNow
        };
    }

    /// <summary>Open → Resolved (người chịu trách nhiệm báo đã sửa).</summary>
    public void Resolve(string? note)
    {
        Require(ChangeRequestStatus.Open, ChangeRequestStatus.Resolved);
        Status = ChangeRequestStatus.Resolved;
        ResolutionNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        ResolvedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Resolved → Verified (Editor xác minh đã đạt).</summary>
    public void Verify(Guid byUserId, string? comment = null)
    {
        Require(ChangeRequestStatus.Resolved, ChangeRequestStatus.Verified);
        Close(ChangeRequestStatus.Verified, byUserId, string.IsNullOrWhiteSpace(comment) ? null : comment.Trim());
    }

    /// <summary>Resolved → Open (Editor thấy chưa đạt); comment bắt buộc.</summary>
    public void Reopen(string comment)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(comment);
        Require(ChangeRequestStatus.Resolved, ChangeRequestStatus.Open);
        Status = ChangeRequestStatus.Open;
        ReviewerComment = comment.Trim();
        ResolvedAt = null;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Open → Rejected (chấp nhận phản đối); lý do bắt buộc.</summary>
    public void Reject(Guid byUserId, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        Require(ChangeRequestStatus.Open, ChangeRequestStatus.Rejected);
        Close(ChangeRequestStatus.Rejected, byUserId, reason.Trim());
    }

    /// <summary>Còn mở thì chặn nộp lại / duyệt: Open và Resolved (chưa xác minh) đều chưa đóng.</summary>
    public bool IsClosed => Status is ChangeRequestStatus.Verified or ChangeRequestStatus.Rejected;

    /// <summary>Trả thông báo lỗi nếu <paramref name="regionJson"/> không phải <c>{x,y,w,h}</c> hợp lệ trong 0–1; null nếu hợp lệ hoặc bỏ trống.</summary>
    public static string? ValidateRegion(string? regionJson)
    {
        if (string.IsNullOrWhiteSpace(regionJson)) return null;

        const string Format = "Vùng phải là JSON { \"x\", \"y\", \"w\", \"h\" } theo tọa độ chuẩn hóa 0–1 (w, h > 0, không vượt khỏi ảnh).";
        try
        {
            using var doc = JsonDocument.Parse(regionJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return Format;

            double Get(string name) =>
                doc.RootElement.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number ? p.GetDouble() : double.NaN;

            double x = Get("x"), y = Get("y"), w = Get("w"), h = Get("h");
            if (double.IsNaN(x) || double.IsNaN(y) || double.IsNaN(w) || double.IsNaN(h)) return Format;
            if (x < 0 || y < 0 || w <= 0 || h <= 0 || x + w > 1.0000001 || y + h > 1.0000001) return Format;
            return null;
        }
        catch (JsonException)
        {
            return Format;
        }
    }

    private void Close(ChangeRequestStatus status, Guid byUserId, string? comment)
    {
        Status = status;
        ClosedByUserId = byUserId;
        ClosedAt = DateTime.UtcNow;
        if (comment is not null) ReviewerComment = comment;
        UpdatedAt = DateTime.UtcNow;
    }

    private void Require(ChangeRequestStatus from, ChangeRequestStatus to)
    {
        if (Status != from)
            throw new InvalidOperationException($"Yêu cầu sửa đang ở trạng thái {Status}, cần {from} để chuyển sang {to}.");
    }
}
