using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.Chapters.Common;
using PanelForge.Application.Features.ScriptLines.Common;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Pages.Commands.SyncScriptStructure;

public sealed record ScriptStructurePanel(int PanelNumber, IReadOnlyList<Guid> ScriptLineIds);

public sealed record ScriptStructurePage(int PageNumber, IReadOnlyList<ScriptStructurePanel> Panels);

/// <summary>
/// Lưu khung Trang → Panel → dòng kịch bản từ trình soạn script trong một transaction: tạo Page/Panel còn thiếu theo số thứ tự
/// và neo dòng vào đúng Panel. Không xoá Page/Panel nào (có thể đã có tranh); trang có trong chương mà kịch bản không nhắc tới
/// được trả về để màn hình cảnh báo.
/// </summary>
public sealed record SyncScriptStructureCommand(Guid ChapterId, Guid UserId, IReadOnlyList<ScriptStructurePage> Pages)
    : IRequest<Result<SyncScriptStructureResult>>;

public sealed record SyncScriptStructureResult(
    int PagesCreated,
    int PanelsCreated,
    int LinesAnchored,
    IReadOnlyList<int> UnlistedPageNumbers);

public sealed class SyncScriptStructureCommandHandler
    : IRequestHandler<SyncScriptStructureCommand, Result<SyncScriptStructureResult>>
{
    private readonly IPanelForgeDbContext _db;
    private readonly IPageRepository _pageRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IWorkspaceAuthorizationService _auth;

    public SyncScriptStructureCommandHandler(
        IPanelForgeDbContext db,
        IPageRepository pageRepository,
        IUnitOfWork unitOfWork,
        IWorkspaceAuthorizationService auth)
    {
        _db = db;
        _pageRepository = pageRepository;
        _unitOfWork = unitOfWork;
        _auth = auth;
    }

    // Marten (event của Page mới) + EF Core ghi chung một transaction: lỗi giữa chừng không để lại khung dở dang
    public Task<Result<SyncScriptStructureResult>> Handle(SyncScriptStructureCommand command, CancellationToken ct)
        => _unitOfWork.ExecuteInTransactionAsync(c => SyncAsync(command, c), ct);

    private async Task<Result<SyncScriptStructureResult>> SyncAsync(SyncScriptStructureCommand command, CancellationToken ct)
    {
        if (!await _db.Chapters.AnyAsync(c => c.Id == command.ChapterId, ct))
            return Failure($"Chapter {command.ChapterId} không tồn tại.", ResultErrorCodes.NotFound);
        if (await ChapterEditGuard.ReadOnlyReasonAsync(_db, command.ChapterId, ct) is { } readOnly)
            return Failure(readOnly, ResultErrorCodes.ChapterReadOnly);
        if (await ScriptLineRules.WriterStructureBlockedReasonAsync(_db, _auth, command.UserId, command.ChapterId, ct) is { } writerBlock)
            return Failure(writerBlock.Error, writerBlock.Code);

        if (Validate(command.Pages) is { } invalid)
            return Failure(invalid);

        var pages = await _db.Pages.Where(p => p.ChapterId == command.ChapterId).ToListAsync(ct);
        var pageIds = pages.Select(p => p.Id).ToList();
        var panels = await _db.Panels.Where(p => pageIds.Contains(p.PageId)).ToListAsync(ct);

        var pagesCreated = 0;
        var panelsCreated = 0;
        var lineTarget = new Dictionary<Guid, Guid>();
        var touchedPanelIds = new HashSet<Guid>();

        foreach (var wantedPage in command.Pages)
        {
            var page = pages.FirstOrDefault(p => p.PageNumber == wantedPage.PageNumber);
            if (page is null)
            {
                page = Page.CreateNew(command.ChapterId, wantedPage.PageNumber, LayoutFormat.StandardPage, 1200, 1800, 300);
                await _pageRepository.SaveAsync(page, ct);
                _db.Pages.Add(page);
                pages.Add(page);
                pagesCreated++;
            }

            foreach (var wantedPanel in wantedPage.Panels)
            {
                var panel = panels.FirstOrDefault(p => p.PageId == page.Id && p.PanelNumber == wantedPanel.PanelNumber);
                if (panel is null)
                {
                    panel = Panel.Create(page.Id, wantedPanel.PanelNumber, wantedPanel.PanelNumber, "{}");
                    panel.CreatedBy = command.UserId.ToString();
                    _db.Panels.Add(panel);
                    panels.Add(panel);
                    panelsCreated++;
                }

                touchedPanelIds.Add(panel.Id);
                foreach (var lineId in wantedPanel.ScriptLineIds)
                    lineTarget[lineId] = panel.Id;
            }
        }

        // Dòng được nhắc tới + dòng đang neo ở các Panel vừa đồng bộ (dòng không còn trong Panel đó thì nhả ra)
        var wantedLineIds = lineTarget.Keys.ToList();
        var lines = await _db.ScriptLines
            .Where(l => wantedLineIds.Contains(l.Id) || (l.PanelId != null && touchedPanelIds.Contains(l.PanelId.Value)))
            .Select(l => new { Line = l, l.Scene.ChapterId })
            .ToListAsync(ct);

        var missing = wantedLineIds.Except(lines.Select(x => x.Line.Id)).ToList();
        if (missing.Count > 0)
            return Failure($"ScriptLine {missing[0]} không tồn tại.");
        if (lines.Any(x => x.ChapterId != command.ChapterId))
            return Failure("ScriptLine và Panel phải thuộc cùng một Chapter.");

        foreach (var x in lines)
            x.Line.AssignToPanel(lineTarget.TryGetValue(x.Line.Id, out var panelId) ? panelId : null);

        await _db.SaveChangesAsync(ct);

        var listed = command.Pages.Select(p => p.PageNumber).ToHashSet();
        var unlisted = pages.Where(p => !listed.Contains(p.PageNumber)).Select(p => p.PageNumber).OrderBy(n => n).ToList();
        return Result<SyncScriptStructureResult>.Success(
            new SyncScriptStructureResult(pagesCreated, panelsCreated, wantedLineIds.Count, unlisted));
    }

    private static string? Validate(IReadOnlyList<ScriptStructurePage> pages)
    {
        if (pages.Any(p => p.PageNumber <= 0) || pages.Select(p => p.PageNumber).Distinct().Count() != pages.Count)
            return "Số trang phải dương và không trùng nhau.";
        foreach (var page in pages)
        {
            if (page.Panels.Any(p => p.PanelNumber <= 0) ||
                page.Panels.Select(p => p.PanelNumber).Distinct().Count() != page.Panels.Count)
                return $"Trang {page.PageNumber}: số panel phải dương và không trùng nhau.";
        }
        var allLines = pages.SelectMany(p => p.Panels).SelectMany(p => p.ScriptLineIds).ToList();
        if (allLines.Distinct().Count() != allLines.Count)
            return "Mỗi dòng kịch bản chỉ được neo vào một Panel.";
        return null;
    }

    private static Result<SyncScriptStructureResult> Failure(string error, string? code = null)
        => Result<SyncScriptStructureResult>.Failure(error, code);
}
