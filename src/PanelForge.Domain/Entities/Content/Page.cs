using PanelForge.Domain.Common;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Enums;
using PanelForge.Domain.Events;

namespace PanelForge.Domain.Entities.Content;

/// <summary>
/// Aggregate Root cho Page — điểm kiểm soát toàn bộ Element.
///
/// Luồng Event Sourcing:
///   Command Handler → page.AddElement(...)
///   → Page.RaiseEvent(new ElementAddedEvent(...))
///   → Page.Apply(ElementAddedEvent) cập nhật _elements dict in-memory
///   → Repository.SaveAsync() append event vào Marten (PostgreSQL)
/// </summary>
public sealed class Page : AggregateRoot
{
    // ── Internal state rebuilt from events ──────────────────────────────────
    public const string DuplicateBindingCode = "ELEMENT_DUPLICATE_BINDING";
    public const string ElementVersionConflictCode = "ELEMENT_VERSION_CONFLICT";

    /// <summary>ElementType (E-30) được phép gắn ScriptLine: balloon, caption, SFX.</summary>
    public static readonly IReadOnlySet<string> TextElementTypes = new HashSet<string>
    {
        "SPEECH_BALLOON", "THOUGHT_BALLOON", "NARRATION_BOX", "SOUND_EFFECT"
    };

    private readonly Dictionary<Guid, ElementState> _elements = new();

    /// <summary>Read-only view của tất cả Elements (kể cả đã removed).</summary>
    public IReadOnlyDictionary<Guid, ElementState> Elements => _elements;

    // ── Canvas metadata ─────────────────────────────────────────────────────
    public Guid ChapterId { get; private set; }
    public int PageNumber { get; private set; }
    public LayoutFormat LayoutFormat { get; private set; }
    public int WidthPx { get; private set; }
    public int HeightPx { get; private set; }
    public int Dpi { get; private set; }
    public Guid? CurrentStageId { get; private set; }

    // ── EF Core Navigation Properties (cho relational queries) ───────────────
    public Chapter Chapter { get; private set; } = default!;
    public PipelineStage? CurrentStage { get; private set; }
    public ICollection<Panel> Panels { get; private set; } = [];

    public void AdvanceToStage(Guid? stageId) => CurrentStageId = stageId;

    // Constructor rỗng bắt buộc để Marten instantiate khi replay stream
    internal Page() { }

    // ── Factory method ──────────────────────────────────────────────────────
    public static Page CreateNew(
        Guid chapterId,
        int pageNumber,
        LayoutFormat layoutFormat,
        int widthPx,
        int heightPx,
        int dpi)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageNumber);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(widthPx);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(heightPx);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dpi);

        var page = new Page();
        page.RaiseEvent(new PageCreatedEvent(
            EventId:      Guid.NewGuid(),
            OccurredAt:   DateTimeOffset.UtcNow,
            PageId:       Guid.NewGuid(),
            ChapterId:    chapterId,
            PageNumber:   pageNumber,
            LayoutFormat: layoutFormat,
            WidthPx:      widthPx,
            HeightPx:     heightPx,
            Dpi:          dpi
        ));
        return page;
    }

    // ══════════════════════════════════════════════════════════════════════════
    // DOMAIN COMMANDS
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Thêm một Element mới vào Page.
    /// → Raises: ElementAddedEvent (APPEND vào stream, KHÔNG INSERT SQL thông thường)
    /// </summary>
    public void AddElement(
        string elementType,
        double x, double y,
        double width, double height,
        int zIndex,
        Guid addedByUserId,
        string? content = null,
        string? assetId = null,
        Guid? scriptLineId = null,
        Guid? speakerId = null,
        string? sourceLineText = null,
        Guid? elementId = null,
        Guid? panelId = null,
        double rotation = 0,
        Guid? bibleEntryId = null)
    {
        if (string.IsNullOrWhiteSpace(elementType))
            throw new ArgumentException("ElementType không được rỗng.", nameof(elementType));
        if (width <= 0 || height <= 0)
            throw new ArgumentException("Width và Height phải > 0.");

        // BR-05: mỗi ScriptLine chỉ gắn tối đa một element đang hoạt động (element đã xóa mềm nhả dòng)
        if (scriptLineId is { } lineId
            && _elements.Values.Any(e => !e.IsRemoved && e.ScriptLineId == lineId))
            throw new InvalidOperationException(
                $"{DuplicateBindingCode}: ScriptLine {lineId} đã được gắn vào một element khác trên Page {Id}.");

        if (elementId is { } requested && (requested == Guid.Empty || _elements.ContainsKey(requested)))
            throw new InvalidOperationException($"ElementId {requested} không hợp lệ hoặc đã tồn tại trên Page {Id}.");

        RaiseEvent(new ElementAddedEvent(
            EventId:       Guid.NewGuid(),
            OccurredAt:    DateTimeOffset.UtcNow,
            PageId:        Id,
            ElementId:     elementId ?? Guid.NewGuid(),
            ElementType:   elementType,
            ZIndex:        zIndex,
            X:             x,
            Y:             y,
            Width:         width,
            Height:        height,
            Content:       content?.Trim(),
            AssetId:       assetId,
            AddedByUserId: addedByUserId,
            ScriptLineId:  scriptLineId,
            SpeakerId:     speakerId,
            RenderedText:  scriptLineId is null ? null : content?.Trim(),
            SourceLineText: sourceLineText,
            PanelId:       panelId,
            Rotation:      rotation == 0 ? null : rotation,
            BibleEntryId:  bibleEntryId
        ));
    }

    /// <summary>
    /// Di chuyển hoặc resize một Element đã tồn tại.
    /// → Raises: ElementMovedEvent (lưu cả vị trí CŨ cho Undo/Diff)
    /// </summary>
    public void MoveElement(
        Guid elementId,
        double newX, double newY,
        double newWidth, double newHeight,
        Guid movedByUserId,
        double? newRotation = null,
        int? newZIndex = null)
    {
        var element = GetActiveElementOrThrow(elementId);

        // Idempotent: không raise event nếu không có gì thay đổi
        if (element.X == newX && element.Y == newY
            && element.Width == newWidth && element.Height == newHeight
            && (newRotation is null || newRotation == element.Rotation)
            && (newZIndex is null || newZIndex == element.ZIndex))
            return;

        RaiseEvent(new ElementMovedEvent(
            EventId:       Guid.NewGuid(),
            OccurredAt:    DateTimeOffset.UtcNow,
            PageId:        Id,
            ElementId:     elementId,
            PreviousX:     element.X,
            PreviousY:     element.Y,
            NewX:          newX,
            NewY:          newY,
            NewWidth:      newWidth,
            NewHeight:     newHeight,
            MovedByUserId: movedByUserId,
            NewRotation:   newRotation,
            NewZIndex:     newZIndex
        ));
    }

    /// <summary>
    /// Xóa mềm (Soft Delete) một Element.
    /// → Raises: ElementRemovedEvent (APPEND vào stream, KHÔNG xóa row nào)
    /// </summary>
    public void RemoveElement(Guid elementId, string reason, Guid removedByUserId)
    {
        GetActiveElementOrThrow(elementId);

        if (string.IsNullOrWhiteSpace(reason))
            reason = "USER_DELETE";

        RaiseEvent(new ElementRemovedEvent(
            EventId:         Guid.NewGuid(),
            OccurredAt:      DateTimeOffset.UtcNow,
            PageId:          Id,
            ElementId:       elementId,
            Reason:          reason,
            RemovedByUserId: removedByUserId
        ));
    }

    /// <summary>
    /// Sửa chữ / làm mới sau khi Writer sửa thoại / gắn lại sang ScriptLine khác (BR-05).
    /// scriptLineId = null nghĩa là element không gắn dòng nào. Giữ unique: một dòng chỉ gắn một element đang hoạt động.
    /// → Raises: ElementTextUpdatedEvent
    /// </summary>
    public void UpdateElementText(
        Guid elementId, string? content, Guid updatedByUserId,
        Guid? scriptLineId = null, Guid? speakerId = null, string? sourceLineText = null)
    {
        var element = GetActiveElementOrThrow(elementId);

        if (scriptLineId is { } lineId)
        {
            if (!TextElementTypes.Contains(element.ElementType))
                throw new InvalidOperationException($"Element loại {element.ElementType} không gắn được ScriptLine.");
            if (_elements.Values.Any(e => !e.IsRemoved && e.ElementId != elementId && e.ScriptLineId == lineId))
                throw new InvalidOperationException(
                    $"{DuplicateBindingCode}: ScriptLine {lineId} đã được gắn vào một element khác trên Page {Id}.");
        }

        RaiseEvent(new ElementTextUpdatedEvent(
            EventId:              Guid.NewGuid(),
            OccurredAt:           DateTimeOffset.UtcNow,
            PageId:               Id,
            ElementId:            elementId,
            Content:              content?.Trim(),
            ScriptLineId:         scriptLineId,
            SpeakerId:            speakerId,
            SourceLineText:       sourceLineText,
            PreviousContent:      element.Content,
            PreviousScriptLineId: element.ScriptLineId,
            UpdatedByUserId:      updatedByUserId));
    }

    /// <summary>BR-20: xác nhận cảnh báo tràn chữ kèm lý do (bắt buộc). → Raises: ElementOverflowAcknowledgedEvent</summary>
    public void AcknowledgeOverflow(Guid elementId, string reason, Guid userId)
    {
        var element = GetActiveElementOrThrow(elementId);
        if (!TextElementTypes.Contains(element.ElementType))
            throw new InvalidOperationException($"Element loại {element.ElementType} không phải element chữ.");
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Phải ghi lý do khi xác nhận cảnh báo tràn chữ.", nameof(reason));
        if (reason.Trim().Length > 500)
            throw new ArgumentException("Lý do tối đa 500 ký tự.", nameof(reason));

        RaiseEvent(new ElementOverflowAcknowledgedEvent(
            Guid.NewGuid(), DateTimeOffset.UtcNow, Id, elementId, reason.Trim(), userId));
    }

    /// <summary>Ghi đè thứ tự đọc (≥ 1) hoặc bỏ ghi đè (null). → Raises: ElementReadingOrderSetEvent</summary>
    public void SetReadingOrder(Guid elementId, int? readingOrder, Guid userId)
    {
        var element = GetActiveElementOrThrow(elementId);
        if (!TextElementTypes.Contains(element.ElementType))
            throw new InvalidOperationException($"Element loại {element.ElementType} không phải element chữ.");
        if (readingOrder is < 1)
            throw new ArgumentException("Thứ tự đọc phải ≥ 1.", nameof(readingOrder));
        if (element.ReadingOrder == readingOrder) return;

        RaiseEvent(new ElementReadingOrderSetEvent(
            Guid.NewGuid(), DateTimeOffset.UtcNow, Id, elementId, readingOrder, element.ReadingOrder, userId));
    }

    /// <summary>Đổi kiểu chữ (null = về mặc định). → Raises: ElementStyleUpdatedEvent</summary>
    public void UpdateElementStyle(Guid elementId, TextStyle? style, Guid userId)
    {
        var element = GetActiveElementOrThrow(elementId);
        if (!TextElementTypes.Contains(element.ElementType))
            throw new InvalidOperationException($"Element loại {element.ElementType} không phải element chữ.");
        var validated = style?.Validated();
        if (element.Style == validated) return;

        RaiseEvent(new ElementStyleUpdatedEvent(
            Guid.NewGuid(), DateTimeOffset.UtcNow, Id, elementId, validated, element.Style, userId));
    }

    /// <summary>
    /// CF4 4.3 / US-12 (BR-01, BR-08): đưa canvas về trạng thái của <paramref name="target"/> (Page dựng lại tới version cũ)
    /// bằng event mới; không xoá event nào. Element đang có mà bản cũ không có → xoá mềm; element khác bản cũ → ElementRestoredEvent.
    /// Xoá trước rồi mới dựng lại nên ràng buộc một ScriptLine – một element (BR-05) giữ như ở bản cũ. Trả số thay đổi.
    /// </summary>
    public int RestoreTo(Page target, Guid restoredByUserId)
    {
        if (target.Id != Id)
            throw new InvalidOperationException("Bản cần khôi phục không thuộc Page này.");
        if (target.Version < 1 || target.Version >= Version)
            throw new InvalidOperationException($"Chỉ khôi phục được về version cũ hơn version hiện tại ({Version}).");
        if (target.IsDeleted)
            throw new InvalidOperationException($"Page đã bị xoá ở version {target.Version}, không khôi phục về bản đó được.");

        var before = UncommittedEvents.Count;
        UpdateCanvasSettings(target.WidthPx, target.HeightPx, target.Dpi, target.LayoutFormat);

        var keep = target.Elements.Values.Where(e => !e.IsRemoved).ToDictionary(e => e.ElementId);
        foreach (var current in _elements.Values.Where(e => !e.IsRemoved && !keep.ContainsKey(e.ElementId)).ToList())
            RemoveElement(current.ElementId, $"RESTORE_TO_VERSION_{target.Version}", restoredByUserId);

        foreach (var old in keep.Values)
        {
            if (_elements.TryGetValue(old.ElementId, out var current) && !current.IsRemoved && SameState(current, old))
                continue;
            RaiseEvent(new ElementRestoredEvent(
                Guid.NewGuid(), DateTimeOffset.UtcNow, Id, old.ElementId, target.Version,
                old.ElementType, old.ZIndex, old.X, old.Y, old.Width, old.Height, old.Rotation,
                old.Content, old.AssetId, old.PanelId, old.BibleEntryId,
                old.ScriptLineId, old.SpeakerId, old.RenderedText, old.SourceLineText,
                old.OverflowAckReason, old.ReadingOrder, old.Style, restoredByUserId));
        }

        return UncommittedEvents.Count - before;
    }

    /// <summary>Hai trạng thái element giống nhau (bỏ qua version và cờ xoá).</summary>
    public static bool SameState(ElementState a, ElementState b) =>
        a.ElementType == b.ElementType && a.ZIndex == b.ZIndex
        && a.X == b.X && a.Y == b.Y && a.Width == b.Width && a.Height == b.Height && a.Rotation == b.Rotation
        && a.Content == b.Content && a.AssetId == b.AssetId && a.PanelId == b.PanelId && a.BibleEntryId == b.BibleEntryId
        && a.ScriptLineId == b.ScriptLineId && a.SpeakerId == b.SpeakerId
        && a.RenderedText == b.RenderedText && a.SourceLineText == b.SourceLineText
        && a.OverflowAckReason == b.OverflowAckReason && a.ReadingOrder == b.ReadingOrder && a.Style == b.Style;

    // ══════════════════════════════════════════════════════════════════════════
    // APPLY — Rebuild state từ events (pure, không có side effects)
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Dispatcher: Marten gọi method này khi replay toàn bộ event stream.
    /// </summary>
    public override void Apply(IDomainEvent domainEvent)
    {
        switch (domainEvent)
        {
            case PageCreatedEvent e:       Apply(e); break;
            case PageCanvasUpdatedEvent e: Apply(e); break;
            case PageReorderedEvent e:     Apply(e); break;
            case ElementAddedEvent e:      Apply(e); break;
            case ElementMovedEvent e:      Apply(e); break;
            case ElementRemovedEvent e:    Apply(e); break;
            case ElementTextUpdatedEvent e:           Apply(e); break;
            case ElementOverflowAcknowledgedEvent e:  Apply(e); break;
            case ElementReadingOrderSetEvent e:       Apply(e); break;
            case ElementStyleUpdatedEvent e:          Apply(e); break;
            case ElementRestoredEvent e:              Apply(e); break;
            case PageDeletedEvent e:       Apply(e); break;
            // Ignore events không nhận ra (schema evolution / forward compatibility)
        }
    }

    internal void Apply(PageCreatedEvent e)
    {
        Id           = e.PageId;
        ChapterId    = e.ChapterId;
        PageNumber   = e.PageNumber;
        LayoutFormat = e.LayoutFormat;
        WidthPx      = e.WidthPx;
        HeightPx     = e.HeightPx;
        Dpi          = e.Dpi;
    }

    internal void Apply(PageCanvasUpdatedEvent e)
    {
        WidthPx      = e.WidthPx;
        HeightPx     = e.HeightPx;
        Dpi          = e.Dpi;
        LayoutFormat = e.LayoutFormat;
    }

    internal void Apply(PageReorderedEvent e)
    {
        PageNumber = e.NewPageNumber;
    }

    internal void Apply(ElementAddedEvent e)
    {
        _elements[e.ElementId] = new ElementState
        {
            ElementId   = e.ElementId,
            ElementType = e.ElementType,
            ZIndex      = e.ZIndex,
            X           = e.X,
            Y           = e.Y,
            Width       = e.Width,
            Height      = e.Height,
            Content     = e.Content,
            AssetId     = e.AssetId,
            ScriptLineId   = e.ScriptLineId,
            SpeakerId      = e.SpeakerId,
            RenderedText   = e.RenderedText,
            SourceLineText = e.SourceLineText,
            PanelId        = e.PanelId,
            Rotation       = e.Rotation ?? 0,
            BibleEntryId   = e.BibleEntryId,
            IsRemoved   = false,
            Version     = 1
        };
    }

    internal void Apply(ElementMovedEvent e)
    {
        if (!_elements.TryGetValue(e.ElementId, out var element)) return;
        // Đổi kích thước hộp làm xác nhận tràn chữ cũ hết hiệu lực
        if (element.Width != e.NewWidth || element.Height != e.NewHeight) element.OverflowAckReason = null;
        element.X      = e.NewX;
        element.Y      = e.NewY;
        element.Width  = e.NewWidth;
        element.Height = e.NewHeight;
        if (e.NewRotation is { } r) element.Rotation = r;
        if (e.NewZIndex is { } z) element.ZIndex = z;
        element.Version++;
    }

    internal void Apply(ElementRemovedEvent e)
    {
        if (!_elements.TryGetValue(e.ElementId, out var element)) return;
        element.IsRemoved = true; // Soft Delete — record vẫn tồn tại trong stream
        element.Version++;
    }

    internal void Apply(ElementTextUpdatedEvent e)
    {
        if (!_elements.TryGetValue(e.ElementId, out var element)) return;
        element.Content           = e.Content;
        element.ScriptLineId      = e.ScriptLineId;
        element.SpeakerId         = e.SpeakerId;
        element.RenderedText      = e.ScriptLineId is null ? null : e.Content;
        element.SourceLineText    = e.SourceLineText;
        element.OverflowAckReason = null; // chữ đổi thì xác nhận cũ hết hiệu lực
        element.Version++;
    }

    internal void Apply(ElementOverflowAcknowledgedEvent e)
    {
        if (!_elements.TryGetValue(e.ElementId, out var element)) return;
        element.OverflowAckReason = e.Reason;
        element.Version++;
    }

    internal void Apply(ElementReadingOrderSetEvent e)
    {
        if (!_elements.TryGetValue(e.ElementId, out var element)) return;
        element.ReadingOrder = e.ReadingOrder;
        element.Version++;
    }

    internal void Apply(ElementStyleUpdatedEvent e)
    {
        if (!_elements.TryGetValue(e.ElementId, out var element)) return;
        element.Style = e.Style;
        element.OverflowAckReason = null; // đổi cỡ/font làm chữ vừa hộp khác đi
        element.Version++;
    }

    internal void Apply(ElementRestoredEvent e)
    {
        var version = _elements.TryGetValue(e.ElementId, out var existing) ? existing.Version : 0;
        _elements[e.ElementId] = new ElementState
        {
            ElementId         = e.ElementId,
            ElementType       = e.ElementType,
            ZIndex            = e.ZIndex,
            X                 = e.X,
            Y                 = e.Y,
            Width             = e.Width,
            Height            = e.Height,
            Rotation          = e.Rotation,
            Content           = e.Content,
            AssetId           = e.AssetId,
            PanelId           = e.PanelId,
            BibleEntryId      = e.BibleEntryId,
            ScriptLineId      = e.ScriptLineId,
            SpeakerId         = e.SpeakerId,
            RenderedText      = e.RenderedText,
            SourceLineText    = e.SourceLineText,
            OverflowAckReason = e.OverflowAckReason,
            ReadingOrder      = e.ReadingOrder,
            Style             = e.Style,
            IsRemoved         = false,
            Version           = version + 1
        };
    }

    internal void Apply(PageDeletedEvent e)
    {
        IsDeleted = true;
        DeletedAt = e.OccurredAt.UtcDateTime;
        DeletedBy = e.DeletedByUserId?.ToString();
    }

    // ── Delete ───────────────────────────────────────────────────────────────
    /// <summary>
    /// Xóa mềm Page.
    /// → Raises: PageDeletedEvent (stream được giữ lại để truy vết lịch sử)
    /// </summary>
    public void Delete(Guid? deletedByUserId)
    {
        if (IsDeleted)
            throw new InvalidOperationException($"Page {Id} đã bị xóa.");

        RaiseEvent(new PageDeletedEvent(
            EventId:         Guid.NewGuid(),
            OccurredAt:      DateTimeOffset.UtcNow,
            PageId:          Id,
            DeletedByUserId: deletedByUserId
        ));
    }

    // ── Canvas settings ──────────────────────────────────────────────────────
    public void UpdateCanvasSettings(int widthPx, int heightPx, int dpi, LayoutFormat layoutFormat)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(widthPx);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(heightPx);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dpi);

        if (WidthPx == widthPx && HeightPx == heightPx && Dpi == dpi && LayoutFormat == layoutFormat)
            return;

        RaiseEvent(new PageCanvasUpdatedEvent(
            EventId:      Guid.NewGuid(),
            OccurredAt:   DateTimeOffset.UtcNow,
            PageId:       Id,
            WidthPx:      widthPx,
            HeightPx:     heightPx,
            Dpi:          dpi,
            LayoutFormat: layoutFormat
        ));
    }

    public void Reorder(int newPageNumber)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(newPageNumber);
        if (PageNumber == newPageNumber) return;

        RaiseEvent(new PageReorderedEvent(
            EventId:       Guid.NewGuid(),
            OccurredAt:    DateTimeOffset.UtcNow,
            PageId:        Id,
            NewPageNumber: newPageNumber
        ));
    }

    // ── Helper ───────────────────────────────────────────────────────────────
    /// <summary>
    /// BR-11: từ chối lệnh nếu element đã bị người khác sửa kể từ lúc client tải (expectedVersion lệch).
    /// expectedVersion = null: không kiểm (client cũ). Sửa element khác trên cùng Page không gây xung đột.
    /// </summary>
    public void EnsureElementVersion(Guid elementId, int? expectedVersion)
    {
        if (expectedVersion is not { } expected) return;
        if (!_elements.TryGetValue(elementId, out var element)) return; // để lệnh chính báo "không tồn tại"
        if (element.Version != expected)
            throw new InvalidOperationException(
                $"{ElementVersionConflictCode}: Xung đột phiên bản: element {elementId} đã được người khác sửa " +
                $"(expected={expected}, actual={element.Version}). Vui lòng tải lại element và chọn giữ bản nào.");
    }

    private ElementState GetActiveElementOrThrow(Guid elementId)
    {
        if (!_elements.TryGetValue(elementId, out var element))
            throw new InvalidOperationException(
                $"Element {elementId} không tồn tại trên Page {Id}.");

        if (element.IsRemoved)
            throw new InvalidOperationException(
                $"Element {elementId} đã bị xóa và không thể thay đổi.");

        return element;
    }
}
