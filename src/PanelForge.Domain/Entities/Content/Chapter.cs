using PanelForge.Domain.Common;
using PanelForge.Domain.Enums;

namespace PanelForge.Domain.Entities.Content;

public class Chapter : BaseEntity
{
    public Guid SeriesId { get; private set; }
    public decimal ChapterNumber { get; private set; }
    public string? Title { get; private set; }
    public DateOnly? TargetReleaseDate { get; private set; }
    /// <summary>V1.2: outline của chương (Producer/Writer ghi), đưa vào input Writer Agent.</summary>
    public string? Outline { get; private set; }

    /// <summary>SHA-256 ảnh bìa chương hiện tại (null = chưa có bìa); file lưu theo hash nên không ghi đè bìa cũ.</summary>
    public string? CoverImageSha256 { get; private set; }
    public long VersionVector { get; private set; } = 0;

    /// <summary>E-02: chương mới là PreProduction (đang viết kịch bản); Gate 1 chuyển sang InProduction.</summary>
    public ChapterStatus Status { get; private set; } = ChapterStatus.PreProduction;
    public DateTime? PublishedAt { get; private set; }

    /// <summary>Producer đã nghiệm thu Gate 3 và kích hoạt bàn giao cho Publisher.</summary>
    public bool IsHandedOff { get; private set; } = false;
    public DateTime? HandedOffAt { get; private set; }
    public Guid? HandedOffById { get; private set; }

    /// <summary>Khóa chương trả phí (false = miễn phí, true = chương trả phí cần mở khóa/VIP).</summary>
    public bool IsLocked { get; private set; } = false;

    /// <summary>Giá mở khóa chương tính bằng VNĐ (0 nếu miễn phí).</summary>
    public decimal PriceVnd { get; private set; } = 0m;

    /// <summary>
    /// Publisher đã chọn tay trang xem trước của chương → không áp mặc định "N trang đầu" của Series nữa.
    /// </summary>
    public bool PreviewPagesCustomized { get; private set; }

    public void MarkPreviewPagesCustomized(bool customized = true) => PreviewPagesCustomized = customized;

    /// <summary>D12: vòng đời kịch bản Draft ↔ InReview → Frozen.</summary>
    public ScriptStatus ScriptStatus { get; private set; } = ScriptStatus.Draft;
    public DateTime? ScriptSubmittedAt { get; private set; }
    public Guid? ScriptSubmittedById { get; private set; }
    /// <summary>Phiên bản kịch bản (ScriptVersion) của lần gửi duyệt gần nhất (giữ cả khi Writer hủy gửi duyệt).</summary>
    public Guid? LastSubmittedVersionId { get; private set; }
    /// <summary>Phiên bản của lần gửi duyệt trước đó, để Editor so sánh thay đổi giữa hai lần gửi.</summary>
    public Guid? PreviousSubmittedVersionId { get; private set; }

    /// <summary>Kịch bản đã đóng băng (= ScriptStatus.Frozen), giữ cột riêng cho các truy vấn cũ.</summary>
    public bool IsScriptFrozen { get; private set; } = false;
    public DateTime? ScriptFrozenAt { get; private set; }
    public Guid? ScriptFrozenById { get; private set; }
    /// <summary>V2.6 (Q26, SCR-41, BIB-14): Phiên bản kịch bản (ScriptVersion) được chốt khi Bắt đầu sản xuất.</summary>
    public Guid? FrozenScriptVersionId { get; private set; }
    // SHA-256 của toàn bộ script-lines lúc đóng băng; null khi chưa đóng băng
    public string? ScriptContentHash { get; private set; }

    /// <summary>V2.5 & WF04: Quản lý trạng thái xin mở lại kịch bản đã đóng băng.</summary>
    public ScriptReopenStatus ScriptReopenStatus { get; private set; } = ScriptReopenStatus.None;
    public string? ScriptReopenReason { get; private set; }
    public Guid? ScriptReopenRequestedById { get; private set; }
    public DateTime? ScriptReopenRequestedAt { get; private set; }

    public Series Series { get; private set; } = default!;
    public ChapterScript? Script { get; private set; }
    public ICollection<Scene> Scenes { get; private set; } = [];
    public ICollection<Page> Pages { get; private set; } = [];

    private Chapter() { }

    public static Chapter Create(
        Guid seriesId,
        decimal chapterNumber,
        string? title = null,
        DateOnly? targetReleaseDate = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(chapterNumber);

        return new Chapter
        {
            SeriesId = seriesId,
            ChapterNumber = chapterNumber,
            Title = title?.Trim(),
            TargetReleaseDate = targetReleaseDate
        };
    }

    public void UpdateDetails(string? title, DateOnly? targetReleaseDate, string? outline = null)
    {
        Title = title?.Trim();
        TargetReleaseDate = targetReleaseDate;
        if (outline != null) Outline = string.IsNullOrWhiteSpace(outline) ? null : outline.Trim();
    }

    /// <summary>Producer/Writer ghi outline chương; chuỗi rỗng = xoá.</summary>
    public void SetOutline(string? outline) => Outline = string.IsNullOrWhiteSpace(outline) ? null : outline.Trim();

    public void IncrementVersion() => VersionVector++;

    public void SetCover(string? sha256) => CoverImageSha256 = sha256;

    /// <summary>Thiết lập khóa trả phí và giá tiền mở khóa của chương.</summary>
    public void SetPricing(bool isLocked, decimal priceVnd)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(priceVnd);
        IsLocked = isLocked;
        PriceVnd = isLocked ? priceVnd : 0m;
    }

    public void AttachScript(ChapterScript script)
    {
        ArgumentNullException.ThrowIfNull(script);
        Script = script;
    }

    /// <summary>Chương còn sửa được nội dung (chưa Approved/Locked/Published).</summary>
    public bool IsContentEditable => Status is ChapterStatus.PreProduction or ChapterStatus.InProduction;

    /// <summary>
    /// Lý do kịch bản đang khoá sửa; chỉ Frozen khoá. Lưu = commit, Nộp = push: khi InReview Editor đọc bản đã nộp
    /// (<see cref="LastSubmittedVersionId"/>) nên Writer vẫn sửa và lưu tiếp, tới lần nộp sau mới đổi bản Editor thấy.
    /// </summary>
    public string? ScriptLockReason => ScriptStatus switch
    {
        ScriptStatus.Frozen => "Kịch bản đã được chốt để sản xuất hình ảnh. Producer cần mở lại kịch bản (kèm lý do) nếu muốn sửa.",
        _ => null
    };

    /// <summary>D12: Writer nộp (push) kịch bản: Draft → InReview, hoặc nộp lại khi đang InReview để Editor thấy bản mới.</summary>
    public void SubmitScript(Guid writerUserId, Guid? scriptVersionId = null)
    {
        RequireEditable("gửi duyệt kịch bản");
        if (ScriptStatus == ScriptStatus.Frozen)
            throw new InvalidOperationException("Kịch bản đã được chốt để sản xuất, không gửi duyệt lại được.");
        ScriptStatus = ScriptStatus.InReview;
        ScriptSubmittedAt = DateTime.UtcNow;
        ScriptSubmittedById = writerUserId;
        // Chụp phiên bản đã gửi; lần gửi trước lùi xuống để so sánh (bỏ qua khi gửi lại đúng bản cũ)
        if (scriptVersionId is { } rev && rev != LastSubmittedVersionId)
        {
            PreviousSubmittedVersionId = LastSubmittedVersionId;
            LastSubmittedVersionId = rev;
        }
        IncrementVersion();
    }

    /// <summary>D12: chỉ Writer rút kịch bản về sửa (InReview → Draft).</summary>
    public void RecallScript(Guid writerUserId)
    {
        RequireEditable("rút kịch bản về sửa");
        RequireScriptStatus(ScriptStatus.InReview, "rút về sửa");
        ScriptStatus = ScriptStatus.Draft;
        ScriptSubmittedAt = null;
        ScriptSubmittedById = null;
        IncrementVersion();
    }

    /// <summary>
    /// D12 / Gate 1: Producer bấm Start Production (InReview → Frozen). Chương PreProduction chuyển sang InProduction;
    /// chương đã InProduction (đóng băng lại sau khi mở) giữ nguyên.
    /// </summary>
    public void StartProduction(Guid producerUserId, string? scriptContentHash, Guid? frozenScriptVersionId = null)
    {
        RequireEditable("bắt đầu sản xuất");
        RequireScriptStatus(ScriptStatus.InReview, "bắt đầu sản xuất");
        ScriptStatus = ScriptStatus.Frozen;
        IsScriptFrozen = true;
        ScriptFrozenAt = DateTime.UtcNow;
        ScriptFrozenById = producerUserId;
        FrozenScriptVersionId = frozenScriptVersionId ?? LastSubmittedVersionId;
        ScriptContentHash = scriptContentHash;
        ScriptReopenStatus = ScriptReopenStatus.None;
        ScriptReopenReason = null;
        ScriptReopenRequestedById = null;
        ScriptReopenRequestedAt = null;
        if (Status == ChapterStatus.PreProduction) Status = ChapterStatus.InProduction;
        IncrementVersion();
    }

    private void ReturnScriptToDraft()
    {
        ScriptStatus = ScriptStatus.Draft;
        IsScriptFrozen = false;
        ScriptFrozenAt = null;
        ScriptFrozenById = null;
        ScriptContentHash = null;
        ScriptSubmittedAt = null;
        ScriptSubmittedById = null;
    }

    /// <summary>D12: Producer mở lại kịch bản đã đóng băng (Frozen → Draft); lý do ghi ở Audit Log của handler.</summary>
    public void UnfreezeScript(Guid userId, string? reason = null)
    {
        RequireStatus(ChapterStatus.InProduction, "mở lại kịch bản");
        RequireScriptStatus(ScriptStatus.Frozen, "mở lại");
        ReturnScriptToDraft();
        ScriptReopenStatus = ScriptReopenStatus.Approved;
        ScriptReopenReason = reason?.Trim();
        ScriptReopenRequestedById = userId;
        ScriptReopenRequestedAt = DateTime.UtcNow;
        IncrementVersion();
    }

    /// <summary>Producer duyệt bản hoàn chỉnh (InProduction → ChapterApproved), sẵn sàng bàn giao cho Publisher.</summary>
    public void Approve()
    {
        RequireStatus(ChapterStatus.InProduction, "duyệt");
        Status = ChapterStatus.ChapterApproved;
        IsHandedOff = false;
        HandedOffAt = null;
        HandedOffById = null;
    }

    /// <summary>Producer kích hoạt bàn giao chương đã nghiệm thu cho Publisher phát hành.</summary>
    public void HandOff(Guid producerUserId)
    {
        RequireStatus(ChapterStatus.ChapterApproved, "bàn giao");
        IsHandedOff = true;
        HandedOffAt = DateTime.UtcNow;
        HandedOffById = producerUserId;
        IncrementVersion();
    }

    /// <summary>Mở lại để sản xuất (ChapterApproved → InProduction). Chương đã HandedOffAndPublished là bản cuối nên không mở lại.</summary>
    public void Reopen()
    {
        RequireStatus(ChapterStatus.ChapterApproved, "mở lại");
        Status = ChapterStatus.InProduction;
        IsHandedOff = false;
        HandedOffAt = null;
        HandedOffById = null;
        IncrementVersion();
    }

    /// <summary>Publisher duyệt xuất bản (ChapterApproved → HandedOffAndPublished sau khi bàn giao). <paramref name="publishAt"/> ở tương lai = lên lịch; Reader chỉ thấy từ lúc đó.</summary>
    public void Publish(DateTime publishAt)
    {
        RequireStatus(ChapterStatus.ChapterApproved, "phát hành");
        if (!IsHandedOff)
            throw new InvalidOperationException("Chương chưa được Producer bàn giao cho Publisher.");
        Status = ChapterStatus.HandedOffAndPublished;
        PublishedAt = publishAt;
        IncrementVersion();
    }

    /// <summary>Reader chỉ thấy chương HandedOffAndPublished đã đến giờ phát hành.</summary>
    public bool IsVisibleToReaders(DateTime utcNow) => Status == ChapterStatus.HandedOffAndPublished && PublishedAt <= utcNow;

    private void RequireStatus(ChapterStatus expected, string action)
    {
        if (Status != expected)
            throw new InvalidOperationException($"Chỉ {action} được chương ở trạng thái {expected}, hiện là {Status}.");
    }

    private void RequireEditable(string action)
    {
        if (!IsContentEditable)
            throw new InvalidOperationException($"Không thể {action}: chương đang ở trạng thái {Status}.");
    }

    private void RequireScriptStatus(ScriptStatus expected, string action)
    {
        if (ScriptStatus != expected)
            throw new InvalidOperationException($"Chỉ {action} được kịch bản ở trạng thái {expected}, hiện là {ScriptStatus}.");
    }
}
