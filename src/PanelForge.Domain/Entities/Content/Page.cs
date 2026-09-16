using PanelForge.Domain.Common;
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
    private readonly Dictionary<Guid, ElementState> _elements = new();

    /// <summary>Read-only view của tất cả Elements (kể cả đã removed).</summary>
    public IReadOnlyDictionary<Guid, ElementState> Elements => _elements;

    // ── Canvas metadata ─────────────────────────────────────────────────────
    public Guid ChapterId { get; private set; }
    public Guid? SceneId { get; private set; }
    public int PageNumber { get; private set; }
    public LayoutFormat LayoutFormat { get; private set; }
    public int WidthPx { get; private set; }
    public int HeightPx { get; private set; }
    public int Dpi { get; private set; }

    // ── EF Core Navigation Properties (cho relational queries) ───────────────
    public Chapter Chapter { get; private set; } = default!;
    public Scene? Scene { get; private set; }
    public ICollection<Panel> Panels { get; private set; } = [];

    // Constructor rỗng bắt buộc để Marten instantiate khi replay stream
    private Page() { }

    // ── Factory method ──────────────────────────────────────────────────────
    public static Page Create(
        Guid chapterId,
        int pageNumber,
        LayoutFormat layoutFormat,
        int widthPx,
        int heightPx,
        int dpi,
        Guid? sceneId = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageNumber);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(widthPx);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(heightPx);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dpi);

        return new Page
        {
            Id           = Guid.NewGuid(),
            ChapterId    = chapterId,
            SceneId      = sceneId,
            PageNumber   = pageNumber,
            LayoutFormat = layoutFormat,
            WidthPx      = widthPx,
            HeightPx     = heightPx,
            Dpi          = dpi
        };
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
        string? assetId = null)
    {
        if (string.IsNullOrWhiteSpace(elementType))
            throw new ArgumentException("ElementType không được rỗng.", nameof(elementType));
        if (width <= 0 || height <= 0)
            throw new ArgumentException("Width và Height phải > 0.");

        RaiseEvent(new ElementAddedEvent(
            EventId:       Guid.NewGuid(),
            OccurredAt:    DateTimeOffset.UtcNow,
            PageId:        Id,
            ElementId:     Guid.NewGuid(),
            ElementType:   elementType,
            ZIndex:        zIndex,
            X:             x,
            Y:             y,
            Width:         width,
            Height:        height,
            Content:       content?.Trim(),
            AssetId:       assetId,
            AddedByUserId: addedByUserId
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
        Guid movedByUserId)
    {
        var element = GetActiveElementOrThrow(elementId);

        // Idempotent: không raise event nếu không có gì thay đổi
        if (element.X == newX && element.Y == newY
            && element.Width == newWidth && element.Height == newHeight)
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
            MovedByUserId: movedByUserId
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
            case ElementAddedEvent e:   Apply(e); break;
            case ElementMovedEvent e:   Apply(e); break;
            case ElementRemovedEvent e: Apply(e); break;
            // Ignore events không nhận ra (schema evolution / forward compatibility)
        }
    }

    private void Apply(ElementAddedEvent e)
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
            IsRemoved   = false
        };
    }

    private void Apply(ElementMovedEvent e)
    {
        if (!_elements.TryGetValue(e.ElementId, out var element)) return;
        element.X      = e.NewX;
        element.Y      = e.NewY;
        element.Width  = e.NewWidth;
        element.Height = e.NewHeight;
    }

    private void Apply(ElementRemovedEvent e)
    {
        if (!_elements.TryGetValue(e.ElementId, out var element)) return;
        element.IsRemoved = true; // Soft Delete — record vẫn tồn tại trong stream
    }

    // ── Canvas settings ──────────────────────────────────────────────────────
    public void UpdateCanvasSettings(int widthPx, int heightPx, int dpi, LayoutFormat layoutFormat)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(widthPx);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(heightPx);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dpi);
        WidthPx      = widthPx;
        HeightPx     = heightPx;
        Dpi          = dpi;
        LayoutFormat = layoutFormat;
    }

    public void AssignToScene(Guid? sceneId) => SceneId = sceneId;

    public void Reorder(int newPageNumber)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(newPageNumber);
        PageNumber = newPageNumber;
    }

    // ── Helper ───────────────────────────────────────────────────────────────
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
