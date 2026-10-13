using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Chapters.Common;
using PanelForge.Application.Common.Interfaces.Persistence;

namespace PanelForge.Application.Features.Panels.Commands.AssignPanelScriptLines;

/// <summary>
/// V1.2: đặt tập ScriptLine neo vào Panel (thay thế tập cũ). Mỗi dòng neo tối đa một Panel nên dòng đang neo
/// ở Panel khác sẽ chuyển sang đây; dòng cũ của Panel không còn trong danh sách bị nhả (PanelId = null).
/// ScriptLine không đổi nội dung, nên phân trang lại chỉ cần gọi lại lệnh này.
/// </summary>
public sealed record AssignPanelScriptLinesCommand(Guid PanelId, IReadOnlyList<Guid> ScriptLineIds, Guid UserId)
    : IRequest<Result<IReadOnlyList<Guid>>>;

public sealed class AssignPanelScriptLinesCommandHandler
    : IRequestHandler<AssignPanelScriptLinesCommand, Result<IReadOnlyList<Guid>>>
{
    private readonly IPanelForgeDbContext _db;

    public AssignPanelScriptLinesCommandHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<IReadOnlyList<Guid>>> Handle(AssignPanelScriptLinesCommand command, CancellationToken ct)
    {
        var panel = await _db.Panels.Include(p => p.Page).FirstOrDefaultAsync(p => p.Id == command.PanelId, ct);
        if (panel is null)
            return Result<IReadOnlyList<Guid>>.Failure($"Panel {command.PanelId} không tồn tại.");
        if (await ChapterEditGuard.ReadOnlyReasonAsync(_db, panel.Page.ChapterId, ct) is { } readOnly)
            return Result<IReadOnlyList<Guid>>.Failure(readOnly, ResultErrorCodes.ChapterReadOnly);

        var wanted = command.ScriptLineIds.Distinct().ToList();
        var lines = await _db.ScriptLines
            .Where(l => wanted.Contains(l.Id) || l.PanelId == panel.Id)
            .Select(l => new { Line = l, ChapterId = l.Scene.ChapterId })
            .ToListAsync(ct);

        var missing = wanted.Except(lines.Select(x => x.Line.Id)).ToList();
        if (missing.Count > 0)
            return Result<IReadOnlyList<Guid>>.Failure($"ScriptLine {missing[0]} không tồn tại.");
        if (lines.Any(x => wanted.Contains(x.Line.Id) && x.ChapterId != panel.Page.ChapterId))
            return Result<IReadOnlyList<Guid>>.Failure("ScriptLine và Panel phải thuộc cùng một Chapter.");

        foreach (var x in lines)
            x.Line.AssignToPanel(wanted.Contains(x.Line.Id) ? panel.Id : null);

        await _db.SaveChangesAsync(ct);
        return Result<IReadOnlyList<Guid>>.Success(wanted);
    }
}
