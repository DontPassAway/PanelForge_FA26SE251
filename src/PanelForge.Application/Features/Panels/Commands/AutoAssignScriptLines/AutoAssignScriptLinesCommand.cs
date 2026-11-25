using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Assignments.Common;
using PanelForge.Application.Features.Chapters.Common;
using PanelForge.Domain.Entities.Content;

namespace PanelForge.Application.Features.Panels.Commands.AutoAssignScriptLines;

/// <summary>
/// Tự phân bổ ScriptLine của một Scene vào các Panel của một Page theo thứ tự đọc.
/// Mỗi dòng chỉ dẫn (không có thoại) mở một cụm mới; các dòng thoại theo sau vào chung cụm đó. Cụm thứ i → Panel thứ i.
/// Chỉ đụng dòng chưa gán hoặc đang gán ở chính Page này (dòng đã nằm ở Page khác giữ nguyên) và Panel chưa gán Scene
/// hoặc đã gán đúng Scene này. DryRun = chỉ trả gợi ý, không ghi.
/// </summary>
public sealed record AutoAssignScriptLinesCommand(Guid PageId, Guid SceneId, bool DryRun, Guid UserId)
    : IRequest<Result<AutoAssignScriptLinesResult>>;

public sealed record PanelLineGroup(Guid PanelId, IReadOnlyList<Guid> ScriptLineIds);

public sealed record AutoAssignScriptLinesResult(
    IReadOnlyList<PanelLineGroup> Assignments,
    IReadOnlyList<Guid> UnassignedLineIds,
    int MissingPanels,
    bool Applied);

public sealed class AutoAssignScriptLinesCommandHandler
    : IRequestHandler<AutoAssignScriptLinesCommand, Result<AutoAssignScriptLinesResult>>
{
    private readonly IPanelForgeDbContext _db;

    public AutoAssignScriptLinesCommandHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<AutoAssignScriptLinesResult>> Handle(AutoAssignScriptLinesCommand command, CancellationToken ct)
    {
        var page = await _db.Pages.AsNoTracking().FirstOrDefaultAsync(p => p.Id == command.PageId, ct);
        if (page is null)
            return Result<AutoAssignScriptLinesResult>.Failure($"Page {command.PageId} không tồn tại.");
        var scene = await _db.Scenes.AsNoTracking().FirstOrDefaultAsync(s => s.Id == command.SceneId, ct);
        if (scene is null)
            return Result<AutoAssignScriptLinesResult>.Failure($"Scene {command.SceneId} không tồn tại.");
        if (scene.ChapterId != page.ChapterId)
            return Result<AutoAssignScriptLinesResult>.Failure("Scene và Page phải thuộc cùng một Chapter.");

        if (!command.DryRun)
        {
            if (await ChapterEditGuard.ReadOnlyReasonAsync(_db, page.ChapterId, ct) is { } readOnly)
                return Result<AutoAssignScriptLinesResult>.Failure(readOnly, ResultErrorCodes.ChapterReadOnly);
            if (await WorkScopeGuard.DenyReasonAsync(_db, command.UserId, page.Id, null, ct) is { } outOfScope)
                return Result<AutoAssignScriptLinesResult>.Failure(outOfScope, ResultErrorCodes.Forbidden);
        }

        var pagePanels = await _db.Panels.Where(p => p.PageId == page.Id).OrderBy(p => p.ReadingOrder).ToListAsync(ct);
        var pagePanelIds = pagePanels.Select(p => p.Id).ToHashSet();
        var panels = pagePanels.Where(p => p.SceneId is null || p.SceneId == scene.Id).ToList();
        var lines = await _db.ScriptLines
            .Where(l => l.SceneId == scene.Id && (l.PanelId == null || pagePanelIds.Contains(l.PanelId.Value)))
            .OrderBy(l => l.LineOrder)
            .ToListAsync(ct);

        var groups = GroupLines(lines);
        var assignments = new List<PanelLineGroup>();
        for (var i = 0; i < Math.Min(groups.Count, panels.Count); i++)
            assignments.Add(new PanelLineGroup(panels[i].Id, groups[i].Select(l => l.Id).ToList()));
        var unassigned = groups.Skip(panels.Count).SelectMany(g => g).Select(l => l.Id).ToList();
        var missing = Math.Max(0, groups.Count - panels.Count);

        if (command.DryRun)
            return Result<AutoAssignScriptLinesResult>.Success(new(assignments, unassigned, missing, false));

        var target = assignments.SelectMany(a => a.ScriptLineIds.Select(id => (LineId: id, a.PanelId)))
            .ToDictionary(x => x.LineId, x => (Guid?)x.PanelId);
        foreach (var line in lines)
            line.AssignToPanel(target.GetValueOrDefault(line.Id));
        foreach (var a in assignments)
            panels.First(p => p.Id == a.PanelId).AssignToScene(scene.Id);

        await _db.SaveChangesAsync(ct);
        return Result<AutoAssignScriptLinesResult>.Success(new(assignments, unassigned, missing, true));
    }

    /// <summary>Dòng chỉ dẫn (có stageDirection, không có thoại) mở cụm mới; dòng thoại nối vào cụm hiện tại.</summary>
    internal static List<List<ScriptLine>> GroupLines(IEnumerable<ScriptLine> lines)
    {
        var groups = new List<List<ScriptLine>>();
        foreach (var line in lines)
        {
            var isAction = string.IsNullOrWhiteSpace(line.DialogueText) && !string.IsNullOrWhiteSpace(line.StageDirection);
            if (groups.Count == 0 || isAction) groups.Add([]);
            groups[^1].Add(line);
        }
        return groups;
    }
}
