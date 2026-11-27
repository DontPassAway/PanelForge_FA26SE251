using PanelForge.Domain.Common;
using PanelForge.Domain.Enums;

namespace PanelForge.Domain.Entities.Preview;

/// <summary>
/// Góp ý nội bộ của Producer/Editor ghim trực tiếp lên trang hoặc khung tranh trong màn Đọc bộ truyện.
/// Append-only: không xoá cứng, chỉ chuyển trạng thái (Open -> Resolved / ConvertedToRework; Resolved -> Open).
/// </summary>
public class PageComment : BaseEntity
{
    public const int MaxMessageLength = 2000;

    public Guid SeriesId { get; private set; }
    public Guid ChapterId { get; private set; }
    public Guid? PageId { get; private set; }
    public Guid? PanelId { get; private set; }
    public Guid? AssetReferenceId { get; private set; }
    public double? PinX { get; private set; }
    public double? PinY { get; private set; }
    public string Message { get; private set; } = default!;
    public Guid AuthorUserId { get; private set; }
    public PageCommentStatus Status { get; private set; } = PageCommentStatus.Open;
    public string? ResolutionNote { get; private set; }
    public Guid? ResolvedByUserId { get; private set; }
    public DateTime? ResolvedAt { get; private set; }
    /// <summary>Ô lưới V3 đã được trả về làm lại từ góp ý này; null nếu chưa chuyển.</summary>
    public Guid? StageTaskId { get; private set; }

    private PageComment() { }

    public static PageComment Create(
        Guid seriesId,
        Guid chapterId,
        Guid authorUserId,
        string message,
        Guid? pageId = null,
        Guid? panelId = null,
        Guid? assetReferenceId = null,
        double? pinX = null,
        double? pinY = null)
    {
        if (seriesId == Guid.Empty)
            throw new ArgumentException("Thiếu SeriesId.", nameof(seriesId));
        if (chapterId == Guid.Empty)
            throw new ArgumentException("Thiếu ChapterId.", nameof(chapterId));
        if (authorUserId == Guid.Empty)
            throw new ArgumentException("Thiếu AuthorUserId.", nameof(authorUserId));

        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        var trimmed = message.Trim();
        if (trimmed.Length > MaxMessageLength)
            throw new ArgumentException($"Góp ý tối đa {MaxMessageLength} ký tự.", nameof(message));

        if (pinX.HasValue != pinY.HasValue)
            throw new ArgumentException("Toạ độ ghim (pinX, pinY) phải cùng có hoặc cùng trống.");

        if (pinX.HasValue)
        {
            if (double.IsNaN(pinX.Value) || double.IsInfinity(pinX.Value) || pinX.Value < 0d || pinX.Value > 1d)
                throw new ArgumentOutOfRangeException(nameof(pinX), pinX.Value, "pinX phải nằm trong khoảng [0, 1].");
            if (double.IsNaN(pinY!.Value) || double.IsInfinity(pinY.Value) || pinY.Value < 0d || pinY.Value > 1d)
                throw new ArgumentOutOfRangeException(nameof(pinY), pinY.Value, "pinY phải nằm trong khoảng [0, 1].");
        }

        return new PageComment
        {
            SeriesId = seriesId,
            ChapterId = chapterId,
            PageId = pageId,
            PanelId = panelId,
            AssetReferenceId = assetReferenceId,
            PinX = pinX,
            PinY = pinY,
            Message = trimmed,
            AuthorUserId = authorUserId,
            Status = PageCommentStatus.Open,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = authorUserId.ToString()
        };
    }

    public void Resolve(Guid byUserId, string? note = null)
    {
        if (byUserId == Guid.Empty)
            throw new ArgumentException("Thiếu người xử lý.", nameof(byUserId));
        if (Status != PageCommentStatus.Open)
            throw new InvalidOperationException("Chỉ góp ý đang mở mới có thể đánh dấu đã xử lý.");

        var now = DateTime.UtcNow;
        Status = PageCommentStatus.Resolved;
        ResolutionNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        ResolvedByUserId = byUserId;
        ResolvedAt = now;
        UpdatedAt = now;
        UpdatedBy = byUserId.ToString();
    }

    public void Reopen(Guid byUserId)
    {
        if (byUserId == Guid.Empty)
            throw new ArgumentException("Thiếu người thực hiện.", nameof(byUserId));
        if (Status != PageCommentStatus.Resolved)
            throw new InvalidOperationException("Chỉ góp ý đã xử lý mới có thể mở lại.");

        Status = PageCommentStatus.Open;
        ResolutionNote = null;
        ResolvedByUserId = null;
        ResolvedAt = null;
        UpdatedAt = DateTime.UtcNow;
        UpdatedBy = byUserId.ToString();
    }

    public void MarkConvertedToRework(Guid stageTaskId, Guid byUserId)
    {
        if (stageTaskId == Guid.Empty)
            throw new ArgumentException("Thiếu ô công việc cần sửa.", nameof(stageTaskId));
        if (byUserId == Guid.Empty)
            throw new ArgumentException("Thiếu người thực hiện.", nameof(byUserId));
        if (Status != PageCommentStatus.Open || StageTaskId.HasValue)
            throw new InvalidOperationException("Chỉ góp ý đang mở và chưa chuyển sửa mới có thể chuyển thành yêu cầu sửa.");

        Status = PageCommentStatus.ConvertedToRework;
        StageTaskId = stageTaskId;
        UpdatedAt = DateTime.UtcNow;
        UpdatedBy = byUserId.ToString();
    }
}
