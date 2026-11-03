using PanelForge.Domain.Common;
using PanelForge.Domain.Entities.Publishing;
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
    public long VersionVector { get; private set; } = 0;

    /// <summary>E-02: chuyển trạng thái do CF5 đảm nhiệm; mặc định InProduction.</summary>
    public ChapterStatus Status { get; private set; } = ChapterStatus.InProduction;
    public DateTime? PublishedAt { get; private set; }

    /// <summary>V2.5: Kịch bản thuần túy được Producer duyệt và đóng băng (Frozen / Read-only) để đưa vào guồng sản xuất tranh.</summary>
    public bool IsScriptFrozen { get; private set; } = false;
    public DateTime? ScriptFrozenAt { get; private set; }
    public Guid? ScriptFrozenById { get; private set; }

    /// <summary>V2.5 & WF04: Quản lý trạng thái xin mở lại kịch bản đã đóng băng.</summary>
    public ScriptReopenStatus ScriptReopenStatus { get; private set; } = ScriptReopenStatus.None;
    public string? ScriptReopenReason { get; private set; }
    public Guid? ScriptReopenRequestedById { get; private set; }
    public DateTime? ScriptReopenRequestedAt { get; private set; }

    public Series Series { get; private set; } = default!;
    public ChapterScript? Script { get; private set; }
    public ICollection<Scene> Scenes { get; private set; } = [];
    public ICollection<Page> Pages { get; private set; } = [];
    public ICollection<ExternalPreviewLink> ExternalPreviewLinks { get; private set; } = [];

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

    public void AttachScript(ChapterScript script)
    {
        ArgumentNullException.ThrowIfNull(script);
        Script = script;
    }

    /// <summary>V2.5: Producer duyệt kịch bản và đóng băng (InProduction, kịch bản thành read-only với Writer).</summary>
    public void FreezeScript(Guid userId)
    {
        RequireStatus(ChapterStatus.InProduction, "đóng băng kịch bản");
        if (IsScriptFrozen)
            throw new InvalidOperationException("Kịch bản của chương này đã được duyệt và đóng băng trước đó.");
        IsScriptFrozen = true;
        ScriptFrozenAt = DateTime.UtcNow;
        ScriptFrozenById = userId;
        ScriptReopenStatus = ScriptReopenStatus.None;
        ScriptReopenReason = null;
        ScriptReopenRequestedById = null;
        ScriptReopenRequestedAt = null;
        IncrementVersion();
    }

    /// <summary>V2.5: Mở lại kịch bản cho Writer sửa (sẽ đánh cờ Stage Impact Flag nếu đã có thumbnail/artwork).</summary>
    /// <summary>V2.5 & WF04: Writer gửi yêu cầu mở lại kịch bản có lý do chính đáng.</summary>
    public void RequestScriptReopen(Guid userId, string reason)
    {
        RequireStatus(ChapterStatus.InProduction, "yêu cầu mở lại kịch bản");
        if (!IsScriptFrozen)
            throw new InvalidOperationException("Kịch bản hiện tại chưa bị đóng băng, không cần yêu cầu mở lại.");
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        ScriptReopenStatus = ScriptReopenStatus.Requested;
        ScriptReopenReason = reason.Trim();
        ScriptReopenRequestedById = userId;
        ScriptReopenRequestedAt = DateTime.UtcNow;
    }

    /// <summary>V2.5 & WF04: Producer duyệt yêu cầu mở lại kịch bản.</summary>
    public void ApproveScriptReopen(Guid producerUserId)
    {
        RequireStatus(ChapterStatus.InProduction, "chấp thuận mở lại kịch bản");
        if (!IsScriptFrozen)
            throw new InvalidOperationException("Kịch bản hiện tại chưa bị đóng băng.");
        if (ScriptReopenStatus != ScriptReopenStatus.Requested)
            throw new InvalidOperationException("Không có yêu cầu mở lại kịch bản nào đang chờ duyệt.");

        IsScriptFrozen = false;
        ScriptReopenStatus = ScriptReopenStatus.Approved;
        ScriptFrozenAt = null;
        ScriptFrozenById = null;
        IncrementVersion();
    }

    /// <summary>V2.5 & WF04: Producer từ chối yêu cầu mở lại kịch bản.</summary>
    public void RejectScriptReopen(Guid producerUserId)
    {
        RequireStatus(ChapterStatus.InProduction, "từ chối mở lại kịch bản");
        if (ScriptReopenStatus != ScriptReopenStatus.Requested)
            throw new InvalidOperationException("Không có yêu cầu mở lại kịch bản nào đang chờ duyệt.");

        ScriptReopenStatus = ScriptReopenStatus.Rejected;
    }

    public void UnfreezeScript(Guid userId)
    {
        RequireStatus(ChapterStatus.InProduction, "mở lại kịch bản");
        if (!IsScriptFrozen)
            throw new InvalidOperationException("Kịch bản của chương này chưa bị đóng băng.");
        IsScriptFrozen = false;
        ScriptFrozenAt = null;
        ScriptFrozenById = null;
        IncrementVersion();
    }

    /// <summary>Producer duyệt bản hoàn chỉnh (InProduction → Approved), chờ Publisher.</summary>
    public void Approve()
    {
        RequireStatus(ChapterStatus.InProduction, "duyệt");
        Status = ChapterStatus.Approved;
    }

    /// <summary>Mở lại để sửa (Approved → InProduction). Chương đã Published là bản cuối nên không mở lại.</summary>
    public void Reopen()
    {
        RequireStatus(ChapterStatus.Approved, "mở lại");
        Status = ChapterStatus.InProduction;
    }

    /// <summary>Publisher duyệt xuất bản (Approved → Published). <paramref name="publishAt"/> ở tương lai = lên lịch; Reader chỉ thấy từ lúc đó.</summary>
    public void Publish(DateTime publishAt)
    {
        RequireStatus(ChapterStatus.Approved, "phát hành");
        Status = ChapterStatus.Published;
        PublishedAt = publishAt;
    }

    /// <summary>Reader chỉ thấy chương Published đã đến giờ phát hành.</summary>
    public bool IsVisibleToReaders(DateTime utcNow) => Status == ChapterStatus.Published && PublishedAt <= utcNow;

    private void RequireStatus(ChapterStatus expected, string action)
    {
        if (Status != expected)
            throw new InvalidOperationException($"Chỉ {action} được chương ở trạng thái {expected}, hiện là {Status}.");
    }
}
