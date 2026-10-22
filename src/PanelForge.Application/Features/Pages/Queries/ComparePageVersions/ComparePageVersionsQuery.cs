using MediatR;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Domain.Entities.Content;

namespace PanelForge.Application.Features.Pages.Queries.ComparePageVersions;

/// <summary>
/// CF4 4.3 (UC-18): so sánh canvas của Page giữa hai version stream. Khớp theo <c>elementId</c>, chỉ xét element đang hoạt động ở
/// từng version: <c>Added</c> (chỉ có bên phải), <c>Removed</c> (chỉ có bên trái), <c>Modified</c> (có cả hai, khác trạng thái).
/// Quyền thành viên Workspace kiểm ở controller (BR-07). Chỉ đọc.
/// </summary>
public sealed record ComparePageVersionsQuery(Guid PageId, long Left, long Right) : IRequest<Result<PageVersionCompareDto>>;

public sealed record PageElementStateDto(
    Guid ElementId, string ElementType, int ZIndex, double X, double Y, double Width, double Height, double Rotation,
    string? Content, string? AssetId, Guid? PanelId, Guid? BibleEntryId, Guid? ScriptLineId, Guid? SpeakerId,
    string? RenderedText, int? ReadingOrder, TextStyle? Style)
{
    public static PageElementStateDto From(ElementState e) => new(
        e.ElementId, e.ElementType, e.ZIndex, e.X, e.Y, e.Width, e.Height, e.Rotation, e.Content, e.AssetId, e.PanelId,
        e.BibleEntryId, e.ScriptLineId, e.SpeakerId, e.RenderedText, e.ReadingOrder, e.Style);
}

/// <param name="Change">Added, Removed, Modified.</param>
/// <param name="ChangedFields">Trường đổi khi Modified (x, y, width, height, rotation, zIndex, content, style...).</param>
public sealed record PageElementDiffDto(
    Guid ElementId, string ElementType, string Change, IReadOnlyList<string> ChangedFields,
    PageElementStateDto? Left, PageElementStateDto? Right);

public sealed record PageCanvasDto(int WidthPx, int HeightPx, int Dpi, string LayoutFormat);

public sealed record PageVersionCompareDto(
    Guid PageId, long Left, long Right, PageCanvasDto LeftCanvas, PageCanvasDto RightCanvas, bool CanvasChanged,
    int Added, int Removed, int Modified, IReadOnlyList<PageElementDiffDto> Elements);

public sealed class ComparePageVersionsQueryHandler : IRequestHandler<ComparePageVersionsQuery, Result<PageVersionCompareDto>>
{
    private readonly IPageRepository _pages;

    public ComparePageVersionsQueryHandler(IPageRepository pages) => _pages = pages;

    public async Task<Result<PageVersionCompareDto>> Handle(ComparePageVersionsQuery q, CancellationToken ct)
    {
        static Result<PageVersionCompareDto> Fail(string m, string c) => Result<PageVersionCompareDto>.Failure(m, c);

        var current = await _pages.GetStreamVersionAsync(q.PageId, ct);
        if (current < 1) return Fail($"Page {q.PageId} không tồn tại.", ResultErrorCodes.NotFound);
        if (q.Left < 1 || q.Right < 1 || q.Left > current || q.Right > current)
            return Fail($"left và right phải trong khoảng 1..{current}.", ResultErrorCodes.Validation);
        if (q.Left == q.Right) return Fail("left và right phải khác nhau.", ResultErrorCodes.Validation);

        var left = await _pages.GetAtVersionAsync(q.PageId, q.Left, ct);
        var right = await _pages.GetAtVersionAsync(q.PageId, q.Right, ct);
        if (left is null || right is null) return Fail($"Page {q.PageId} không tồn tại.", ResultErrorCodes.NotFound);

        var l = left.Elements.Values.Where(e => !e.IsRemoved).ToDictionary(e => e.ElementId);
        var r = right.Elements.Values.Where(e => !e.IsRemoved).ToDictionary(e => e.ElementId);
        var diffs = new List<PageElementDiffDto>();
        foreach (var id in l.Keys.Union(r.Keys))
        {
            l.TryGetValue(id, out var a);
            r.TryGetValue(id, out var b);
            if (a is null)
                diffs.Add(new(id, b!.ElementType, "Added", [], null, PageElementStateDto.From(b)));
            else if (b is null)
                diffs.Add(new(id, a.ElementType, "Removed", [], PageElementStateDto.From(a), null));
            else if (!Page.SameState(a, b))
                diffs.Add(new(id, b.ElementType, "Modified", ChangedFields(a, b), PageElementStateDto.From(a), PageElementStateDto.From(b)));
        }

        var leftCanvas = Canvas(left);
        var rightCanvas = Canvas(right);
        return Result<PageVersionCompareDto>.Success(new PageVersionCompareDto(
            q.PageId, q.Left, q.Right, leftCanvas, rightCanvas, leftCanvas != rightCanvas,
            diffs.Count(d => d.Change == "Added"), diffs.Count(d => d.Change == "Removed"), diffs.Count(d => d.Change == "Modified"),
            diffs.OrderBy(d => d.Change).ThenBy(d => (d.Right ?? d.Left)!.ZIndex).ToList()));
    }

    private static PageCanvasDto Canvas(Page p) => new(p.WidthPx, p.HeightPx, p.Dpi, p.LayoutFormat.ToString());

    private static List<string> ChangedFields(ElementState a, ElementState b)
    {
        var fields = new List<string>();
        void Check(string name, bool same) { if (!same) fields.Add(name); }
        Check("elementType", a.ElementType == b.ElementType);
        Check("x", a.X == b.X);
        Check("y", a.Y == b.Y);
        Check("width", a.Width == b.Width);
        Check("height", a.Height == b.Height);
        Check("rotation", a.Rotation == b.Rotation);
        Check("zIndex", a.ZIndex == b.ZIndex);
        Check("content", a.Content == b.Content && a.RenderedText == b.RenderedText);
        Check("assetId", a.AssetId == b.AssetId);
        Check("panelId", a.PanelId == b.PanelId);
        Check("bibleEntryId", a.BibleEntryId == b.BibleEntryId);
        Check("scriptLineId", a.ScriptLineId == b.ScriptLineId && a.SpeakerId == b.SpeakerId && a.SourceLineText == b.SourceLineText);
        Check("readingOrder", a.ReadingOrder == b.ReadingOrder);
        Check("style", a.Style == b.Style);
        Check("overflowAck", a.OverflowAckReason == b.OverflowAckReason);
        return fields;
    }
}
