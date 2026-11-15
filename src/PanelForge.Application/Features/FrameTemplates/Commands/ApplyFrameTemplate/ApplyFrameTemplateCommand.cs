using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Assignments.Common;
using PanelForge.Application.Features.Panels.Models;
using PanelForge.Application.Features.Panels.Services;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.FrameTemplates.Commands.ApplyFrameTemplate;

/// <summary>
/// Artist dựng Page từ frame template (CF2 bước 9): tạo một Panel cho mỗi khung, SAO CHÉP hình học vào BoundingBox.
/// Thay thế bố cục hiện tại và giữ dữ liệu theo ReadingOrder. Thứ tự đọc tính theo ReadingDirection của Series.
/// </summary>
public sealed record ApplyFrameTemplateCommand(Guid PageId, Guid TemplateId, Guid UserId)
    : IRequest<Result<PageLayoutDto>>;

public sealed class ApplyFrameTemplateCommandHandler
    : IRequestHandler<ApplyFrameTemplateCommand, Result<PageLayoutDto>>
{
    private readonly IPanelForgeDbContext _db;
    private readonly IPageRepository _pages;
    private readonly PageLayoutReplacer _replacer;

    public ApplyFrameTemplateCommandHandler(IPanelForgeDbContext db, IPageRepository pages, PageLayoutReplacer replacer)
    {
        _db = db;
        _pages = pages;
        _replacer = replacer;
    }

    public async Task<Result<PageLayoutDto>> Handle(ApplyFrameTemplateCommand cmd, CancellationToken ct)
    {
        static Result<PageLayoutDto> Fail(string msg, string code) => Result<PageLayoutDto>.Failure(msg, code);

        var page = await (
            from p in _db.Pages
            join c in _db.Chapters on p.ChapterId equals c.Id
            join s in _db.Series on c.SeriesId equals s.Id
            where p.Id == cmd.PageId
            select new { p.WidthPx, p.HeightPx, p.LayoutFormat, c.Status, s.WorkspaceId, s.ReadingDirection })
            .FirstOrDefaultAsync(ct);
        if (page is null) return Fail($"Page {cmd.PageId} không tồn tại.", ResultErrorCodes.NotFound);

        // Template có sẵn hoặc template của CHÍNH Workspace chứa Page (không dùng được template của Workspace khác)
        var template = FrameTemplate.BuiltIns.FirstOrDefault(t => t.Id == cmd.TemplateId)
            ?? await _db.FrameTemplates.AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == cmd.TemplateId && t.WorkspaceId == page.WorkspaceId, ct);
        if (template is null) return Fail("Frame template không tồn tại.", ResultErrorCodes.NotFound);

        if (page.Status != ChapterStatus.InProduction)
            return Fail("Chương không còn ở trạng thái sản xuất, không dựng lại bố cục được (BR-12).", ResultErrorCodes.Conflict);
        if (await WorkScopeGuard.DenyReasonAsync(_db, cmd.UserId, cmd.PageId, null, ct) is { } outOfScope)
            return Fail(outOfScope, ResultErrorCodes.Forbidden);
        if (template.LayoutFormat == LayoutFormat.SpreadDouble && page.LayoutFormat != LayoutFormat.SpreadDouble)
            return Fail($"Template dành cho trang đôi (SpreadDouble), không thể áp dụng cho trang đơn ({page.LayoutFormat}).", ResultErrorCodes.Validation);

        var pageAggregate = await _pages.GetAsync(cmd.PageId, ct);
        if (pageAggregate is null)
            return Fail($"Page {cmd.PageId} không tồn tại.", ResultErrorCodes.NotFound);

        var panels = new List<Panel>();
        foreach (var (slot, order) in OrderForReading(template.Slots, page.ReadingDirection))
        {
            var bbox = JsonSerializer.Serialize(new
            {
                x = (int)Math.Round(slot.X * page.WidthPx), y = (int)Math.Round(slot.Y * page.HeightPx),
                width = (int)Math.Round(slot.W * page.WidthPx), height = (int)Math.Round(slot.H * page.HeightPx),
                unit = "px", pageWidthPx = page.WidthPx, pageHeightPx = page.HeightPx,
                normalized = new { x = slot.X, y = slot.Y, w = slot.W, h = slot.H, coordinateSpace = "page" },
                corners = slot.Corners?.Select(c => new { x = c.X, y = c.Y }),
                borderless = slot.Borderless,
                frameTemplate = new { id = template.Id, name = template.Name }
            });
            var panel = Panel.Create(cmd.PageId, order, order, bbox);
            panel.CreatedBy = cmd.UserId.ToString();
            panels.Add(panel);
        }

        var replacement = await _replacer.ReplaceAsync(pageAggregate, panels, cmd.UserId, ct);
        var panelDtos = replacement.Panels.Select(p => new PanelDto(
            p.Id, p.PageId, p.PanelNumber, p.BoundingBox, p.ReadingOrder, p.CurrentStageId,
            p.CreatedAt, p.UpdatedAt, p.CreatedBy, p.SceneId, p.Version, p.Direction)).ToList();
        return Result<PageLayoutDto>.Success(new PageLayoutDto(panelDtos, replacement.UnassignedArtworkCount));
    }

    /// <summary>
    /// Thứ tự đọc là hàm của ReadingDirection: chia khung thành hàng từ trên xuống (khung bắt đầu trước khi nửa trên của
    /// hàng hiện tại kết thúc thì cùng hàng), trong hàng xếp theo hướng đọc (RightToLeft: phải → trái; còn lại: trái → phải).
    /// </summary>
    public static IEnumerable<(FrameSlot Slot, int Order)> OrderForReading(IReadOnlyList<FrameSlot> slots, ReadingDirection dir)
    {
        var rows = new List<List<FrameSlot>>();
        foreach (var s in slots.OrderBy(s => s.Y).ThenBy(s => s.X))
        {
            var row = rows.LastOrDefault();
            if (row is not null && s.Y < row.Min(r => r.Y + r.H / 2)) row.Add(s);
            else rows.Add([s]);
        }

        var order = 0;
        foreach (var row in rows)
        {
            var sorted = dir == ReadingDirection.RightToLeft
                ? row.OrderByDescending(s => s.X + s.W)
                : row.OrderBy(s => s.X);
            foreach (var s in sorted) yield return (s, ++order);
        }
    }
}
