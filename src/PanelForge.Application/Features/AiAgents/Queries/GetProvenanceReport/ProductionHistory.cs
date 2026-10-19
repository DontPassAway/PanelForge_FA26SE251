using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Domain.Common;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;
using PanelForge.Domain.Events;

namespace PanelForge.Application.Features.AiAgents.Queries.GetProvenanceReport;

/// <summary>
/// Phần "người làm" của Provenance Report (CF4, BR-10): phiên bản artwork của Artist (AssetReferences), lịch sử chèn chữ
/// của Letterer (event stream của Page) và các lần nộp / duyệt / trả về (Submissions, Editor). Chỉ đọc dữ liệu
/// append-only có sẵn, không tạo bảng. Mỗi mục là node <c>Revision</c> với <c>kind</c> = Artwork | Lettering | Review
/// để màn truy vết dùng chung bộ chọn và dòng thời gian với revision kịch bản.
/// </summary>
internal sealed class ProductionHistory
{
    private const string MoveAction = "đổi vị trí/kích thước";

    private sealed record Item(
        Guid Id, DateTime At, string Kind, string Role, int VersionNumber, string Summary, Guid ByUserId, object Details,
        Guid? PreviousId, string TimelineType);

    private sealed record LetteringDetails(Guid PageId, int PageNumber, Guid ElementId, string Action, int Count);

    private readonly List<Item> _items = [];

    public static readonly ProductionHistory Empty = new();

    public IEnumerable<Guid> UserIds => _items.Select(i => i.ByUserId);

    public static async Task<ProductionHistory> LoadAsync(
        IPanelForgeDbContext db, IPageRepository pages, Guid chapterId, CancellationToken ct)
    {
        var history = new ProductionHistory();
        var pageNumbers = await db.Pages.AsNoTracking().Where(p => p.ChapterId == chapterId)
            .ToDictionaryAsync(p => p.Id, p => p.PageNumber, ct);
        string PageLabel(Guid pageId) => pageNumbers.TryGetValue(pageId, out var n) ? $"Trang {n}" : "Trang ?";

        var artworks = await db.AssetReferences.AsNoTracking().Where(r => r.ChapterId == chapterId)
            .OrderBy(r => r.VersionNumber).ThenBy(r => r.CreatedAt).ToListAsync(ct); // nối phiên bản theo số v
        var assignments = await db.Assignments.AsNoTracking().Where(a => a.ChapterId == chapterId).ToListAsync(ct);
        var stageIds = artworks.Select(a => a.PipelineStageId).Concat(assignments.Select(a => a.PipelineStageId))
            .Distinct().ToList();
        var stageNames = await db.PipelineStages.AsNoTracking().Where(s => stageIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        string Stage(Guid id) => stageNames.GetValueOrDefault(id) ?? "?";

        // ── Artist: mỗi bản artwork tải lên là một phiên bản (theo trang/khung/stage) ──
        var lastArtwork = new Dictionary<(Guid Page, Guid? Panel, Guid Stage), Guid>();
        foreach (var a in artworks)
        {
            var key = (a.PageId, a.PanelId, a.PipelineStageId);
            var target = a.PanelId is null ? PageLabel(a.PageId) : $"{PageLabel(a.PageId)} · khung";
            history._items.Add(new Item(a.Id, a.CreatedAt, "Artwork", "Artist", a.VersionNumber,
                $"Artwork {Stage(a.PipelineStageId)} — {target}: {a.FileName}", a.UploadedByUserId,
                new
                {
                    a.PageId, pageNumber = pageNumbers.GetValueOrDefault(a.PageId), a.PanelId,
                    stage = Stage(a.PipelineStageId), a.FileName, a.LayerType
                },
                lastArtwork.TryGetValue(key, out var prev) ? prev : null, "ArtworkUploaded"));
            lastArtwork[key] = a.Id;
        }

        // ── Letterer: event chữ trên từng trang; chuỗi kéo/đổi cỡ liên tiếp của cùng bóng gộp thành một mục ──
        foreach (var (pageId, pageNumber) in pageNumbers.OrderBy(p => p.Value))
        {
            var events = await pages.GetEventsAsync(pageId, ct) ?? [];
            var textIds = new HashSet<Guid>();
            var lastByElement = new Dictionary<Guid, int>(); // elementId → vị trí mục cuối trong _items
            var versions = new Dictionary<Guid, int>();
            foreach (var e in events)
            {
                if (e is ElementAddedEvent added && Page.TextElementTypes.Contains(added.ElementType))
                    textIds.Add(added.ElementId);
                var (elementId, by, action) = Describe(e);
                if (elementId is not { } id || !textIds.Contains(id)) continue;

                var version = versions[id] = versions.GetValueOrDefault(id) + 1;
                var hasLast = lastByElement.TryGetValue(id, out var lastIndex);
                if (e is ElementMovedEvent && hasLast && lastIndex == history._items.Count - 1
                    && history._items[lastIndex] is { Details: LetteringDetails { Action: MoveAction } d } last
                    && last.ByUserId == by)
                {
                    var count = d.Count + 1;
                    history._items[lastIndex] = last with
                    {
                        At = e.OccurredAt.UtcDateTime, VersionNumber = version, Details = d with { Count = count },
                        Summary = $"{PageLabel(pageId)} · bóng {Short(id)}: {MoveAction} ({count} lần)"
                    };
                    continue;
                }

                history._items.Add(new Item(e.EventId, e.OccurredAt.UtcDateTime, "Lettering", "Letterer", version,
                    $"{PageLabel(pageId)} · bóng {Short(id)}: {action}", by,
                    new LetteringDetails(pageId, pageNumber, id, action, 1),
                    hasLast ? history._items[lastIndex].Id : null, "LetteringChanged"));
                lastByElement[id] = history._items.Count - 1;
            }
        }

        // ── Editor: mỗi lần nộp và quyết định duyệt / trả về ──
        var byAssignment = assignments.ToDictionary(a => a.Id);
        var submissions = await db.Submissions.AsNoTracking().Where(s => s.ChapterId == chapterId)
            .OrderBy(s => s.SubmittedAt).ToListAsync(ct);
        var lastSubmission = new Dictionary<Guid, Guid>();
        foreach (var s in submissions)
        {
            var a = byAssignment.GetValueOrDefault(s.AssignmentId);
            var work = a is null ? "việc" : $"{a.AssignedRole} · {Stage(a.PipelineStageId)} ({a.EntityType})";
            var decision = s.Status switch
            {
                SubmissionStatus.Approved => "đã duyệt",
                SubmissionStatus.Rejected => "trả về",
                SubmissionStatus.Superseded => "bị thay bằng lần nộp sau",
                _ => "chờ duyệt"
            };
            var comment = string.IsNullOrWhiteSpace(s.DecisionComment) ? "" : $" — \"{s.DecisionComment}\"";
            history._items.Add(new Item(s.Id, s.DecidedAt ?? s.SubmittedAt, "Review", "Editor", s.AttemptNumber,
                $"Nộp lần {s.AttemptNumber} — {work}: {decision}{comment}", s.DecidedByUserId ?? s.SubmittedByUserId,
                new
                {
                    s.AssignmentId, role = a?.AssignedRole.ToString(), stage = a is null ? null : Stage(a.PipelineStageId),
                    s.AttemptNumber, status = s.Status.ToString(), s.SubmittedAt, s.SubmittedByUserId,
                    s.DecidedAt, s.DecidedByUserId, s.DecisionComment, s.ChapterVersion, s.ArtworkReferenceIds
                },
                lastSubmission.TryGetValue(s.AssignmentId, out var prev) ? prev : null,
                s.DecidedAt is null ? "WorkSubmitted" : "ReviewDecided"));
            lastSubmission[s.AssignmentId] = s.Id;
        }

        return history;
    }

    private static string Short(Guid id) => id.ToString()[..8];

    private static (Guid? ElementId, Guid By, string Action) Describe(IDomainEvent e) => e switch
    {
        ElementAddedEvent x => (x.ElementId, x.AddedByUserId, $"đặt bóng \"{Clip(x.RenderedText ?? x.Content)}\""),
        ElementTextUpdatedEvent x => (x.ElementId, x.UpdatedByUserId, $"sửa chữ \"{Clip(x.Content)}\""),
        ElementStyleUpdatedEvent x => (x.ElementId, x.UpdatedByUserId, x.Style is null
            ? "về kiểu mặc định"
            : $"đổi kiểu {x.Style.FontFamily} {x.Style.FontSize}px{(x.Style.BalloonShape is { } s ? $", bóng {s}" : "")}"),
        ElementMovedEvent x => (x.ElementId, x.MovedByUserId, MoveAction),
        ElementReadingOrderSetEvent x => (x.ElementId, x.SetByUserId, $"thứ tự đọc {x.ReadingOrder?.ToString() ?? "bỏ"}"),
        ElementOverflowAcknowledgedEvent x => (x.ElementId, x.AcknowledgedByUserId, $"xác nhận tràn chữ: {x.Reason}"),
        ElementRemovedEvent x => (x.ElementId, x.RemovedByUserId, $"xoá bóng ({x.Reason})"),
        _ => (null, Guid.Empty, "")
    };

    private static string Clip(string? text) =>
        string.IsNullOrEmpty(text) ? "" : text.Length <= 40 ? text : text[..40] + "…";

    public void AppendTo(
        List<ProvenanceNode> nodes, List<ProvenanceEdge> edges, List<ProvenanceTimelineEntry> timeline, Func<Guid, string> name)
    {
        foreach (var i in _items)
        {
            nodes.Add(new ProvenanceNode("Revision", i.Id, i.At, $"v{i.VersionNumber} [{i.Kind}] {i.Summary}", new
            {
                kind = i.Kind, role = i.Role, versionNumber = i.VersionNumber, origin = "Human",
                createdBy = name(i.ByUserId), createdByUserId = i.ByUserId, production = i.Details
            }));
            if (i.PreviousId is { } prev) edges.Add(new ProvenanceEdge(prev, i.Id, "previousVersion"));
            timeline.Add(new ProvenanceTimelineEntry(i.At, i.TimelineType, $"{name(i.ByUserId)} ({i.Role}): {i.Summary}", i.Id, null));
        }
    }
}
