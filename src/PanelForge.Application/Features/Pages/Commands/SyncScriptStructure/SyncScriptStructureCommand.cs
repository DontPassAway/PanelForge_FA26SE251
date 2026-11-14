using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.Assignments.Common;
using PanelForge.Application.Features.Chapters.Common;
using PanelForge.Application.Features.ScriptLines.Common;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Pages.Commands.SyncScriptStructure;

public sealed record PanelCastRevisionItem(Guid BibleEntryId, Guid RevisionId);

public sealed record ScriptStructurePanel(
    int PanelNumber,
    IReadOnlyList<Guid> ScriptLineIds,
    IReadOnlyList<PanelCastRevisionItem>? CastRevisions = null);

public sealed record ScriptStructurePage(int PageNumber, IReadOnlyList<ScriptStructurePanel> Panels);

/// <summary>
/// Lưu khung Trang → Panel → dòng kịch bản từ trình soạn script trong một transaction: tạo Page/Panel còn thiếu theo số thứ tự,
/// neo dòng vào đúng Panel, và xoá mềm Page/Panel kịch bản không còn nhắc tới. Kịch bản là nguồn của khung: Bàn vẽ chỉ làm
/// sau khi kịch bản chốt nên chưa có tranh để mất. Page/Panel còn việc đang giao thì từ chối (Conflict) thay vì xoá.
/// </summary>
public sealed record SyncScriptStructureCommand(Guid ChapterId, Guid UserId, IReadOnlyList<ScriptStructurePage> Pages)
    : IRequest<Result<SyncScriptStructureResult>>;

public sealed record SyncScriptStructureResult(
    int PagesCreated,
    int PanelsCreated,
    int LinesAnchored,
    int PagesRemoved,
    int PanelsRemoved);

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
        var authorized = await ScriptLineRules.AuthorizeAsync(_db, _auth, command.UserId, command.ChapterId, null, ct);
        if (!authorized.IsSuccess)
            return Failure(authorized.ErrorMessage!, authorized.ErrorCode);

        if (Validate(command.Pages) is { } invalid)
            return Failure(invalid);

        // BIB-11, Q23, Q24: Validate castRevisions
        var allCastRevisions = command.Pages
            .SelectMany(p => p.Panels)
            .Where(pan => pan.CastRevisions is { Count: > 0 })
            .SelectMany(pan => pan.CastRevisions!)
            .ToList();
        if (await ScriptStructureRules.ValidateCastRevisionsAsync(_db, command.ChapterId, allCastRevisions, ct) is { } castError)
            return Failure(castError, ResultErrorCodes.Validation);

        var pages = await _db.Pages.Where(p => p.ChapterId == command.ChapterId).ToListAsync(ct);
        var pageIds = pages.Select(p => p.Id).ToList();
        var panels = await _db.Panels.Where(p => pageIds.Contains(p.PageId)).ToListAsync(ct);

        var pagesCreated = 0;
        var panelsCreated = 0;
        var lineTarget = new Dictionary<Guid, Guid>();
        var touchedPanelIds = new HashSet<Guid>();

        foreach (var wantedPage in command.Pages)
        {
            var (page, pageCreated) = await ScriptStructureRules.EnsurePageAsync(_db, _pageRepository, pages, command.ChapterId, wantedPage.PageNumber, ct);
            if (pageCreated) pagesCreated++;

            foreach (var wantedPanel in wantedPage.Panels)
            {
                var (panel, panelCreated) = ScriptStructureRules.EnsurePanel(_db, panels, page.Id, wantedPanel.PanelNumber, command.UserId);
                if (panelCreated) panelsCreated++;

                touchedPanelIds.Add(panel.Id);
                foreach (var lineId in wantedPanel.ScriptLineIds)
                    lineTarget[lineId] = panel.Id;
            }
        }

        // Page / Panel kịch bản không còn nhắc tới
        var listedPageNumbers = command.Pages.Select(p => p.PageNumber).ToHashSet();
        var removedPages = pages.Where(p => !listedPageNumbers.Contains(p.PageNumber)).ToList();
        var removedPanels = panels.Where(p => !touchedPanelIds.Contains(p.Id)).ToList();
        var removedPanelIds = removedPanels.Select(p => p.Id).ToHashSet();

        var removedTargets = removedPages.Select(p => p.Id).Concat(removedPanelIds).ToList();
        if (removedTargets.Count > 0 &&
            await AssignmentRules.DeleteBlockedReasonAsync(_db, "trang", removedTargets, ct) is { } blocked)
            return Failure(blocked, ResultErrorCodes.Conflict);

        // Dòng được nhắc tới + dòng đang neo ở Panel vừa đồng bộ hoặc sắp xoá (dòng không còn trong Panel đó thì nhả ra)
        var wantedLineIds = lineTarget.Keys.ToList();
        var lines = await _db.ScriptLines
            .Where(l => wantedLineIds.Contains(l.Id) ||
                        (l.PanelId != null &&
                         (touchedPanelIds.Contains(l.PanelId.Value) || removedPanelIds.Contains(l.PanelId.Value))))
            .Select(l => new { Line = l, l.Scene.ChapterId })
            .ToListAsync(ct);

        var missing = wantedLineIds.Except(lines.Select(x => x.Line.Id)).ToList();
        if (missing.Count > 0)
            return Failure($"ScriptLine {missing[0]} không tồn tại.");
        if (lines.Any(x => x.ChapterId != command.ChapterId))
            return Failure("ScriptLine và Panel phải thuộc cùng một Chapter.");

        foreach (var x in lines)
            x.Line.AssignToPanel(lineTarget.TryGetValue(x.Line.Id, out var panelId) ? panelId : null);

        // Xoá mềm như DeletePanel / DeletePage, gồm cả panel_bible_variants mồ côi
        await ScriptStructureRules.RemoveAsync(_db, _pageRepository, removedPages, removedPanels, command.UserId, ct);

        // BIB-11, Q23, Q24: ghi lại panel_bible_variants của các Panel được giữ
        var activePanelIds = touchedPanelIds.ToList();
        var existingVariants = await _db.PanelBibleVariants.Where(v => activePanelIds.Contains(v.PanelId)).ToListAsync(ct);
        var variantsByPanel = existingVariants.GroupBy(v => v.PanelId).ToDictionary(g => g.Key, g => g.ToList());

        foreach (var wantedPage in command.Pages)
        {
            var page = pages.First(p => p.PageNumber == wantedPage.PageNumber);
            foreach (var wantedPanel in wantedPage.Panels)
            {
                if (wantedPanel.CastRevisions is null) continue;
                var panel = panels.First(p => p.PageId == page.Id && p.PanelNumber == wantedPanel.PanelNumber);
                ScriptStructureRules.ApplyCastRevisions(
                    _db, panel.Id, wantedPanel.CastRevisions, variantsByPanel.GetValueOrDefault(panel.Id, []), command.UserId);
            }
        }

        await _db.SaveChangesAsync(ct);

        return Result<SyncScriptStructureResult>.Success(new SyncScriptStructureResult(
            pagesCreated, panelsCreated, wantedLineIds.Count, removedPages.Count, removedPanels.Count));
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
