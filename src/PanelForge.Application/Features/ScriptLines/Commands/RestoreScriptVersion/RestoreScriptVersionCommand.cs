using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.Chapters.Services;
using PanelForge.Application.Features.ScriptLines.Commands.SaveChapterScript;
using PanelForge.Application.Features.ScriptLines.Models;
using PanelForge.Application.Features.ScriptVersions;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.ScriptLines.Commands.RestoreScriptVersion;

/// <summary>
/// SCR-23 (Q25/Q29: <c>ScriptVersion</c> là nguồn duy nhất của lịch sử kịch bản): Writer khôi phục cả kịch bản chương về một
/// phiên bản cũ. Không sửa phiên bản nào (BR-01): dựng lại cây Trang → Panel → dòng từ ảnh chụp rồi lưu qua đúng đường
/// <see cref="ChapterScriptSaver"/> của lần lưu tay (quyền Writer, Frozen, <c>expectedRevisionId</c>, chặn xoá trang đang sản xuất),
/// nên kết quả là một <c>ScriptVersion</c> MỚI ở đầu lịch sử (BR-08).
/// </summary>
/// <param name="ExpectedRevisionId">Phiên bản mới nhất client đang thấy; lệch → 409 như lần lưu thường.</param>
public sealed record RestoreScriptVersionCommand(Guid ChapterId, Guid VersionId, Guid UserId, Guid? ExpectedRevisionId)
    : IRequest<Result<SaveChapterScriptResult>>;

public sealed class RestoreScriptVersionCommandHandler
    : IRequestHandler<RestoreScriptVersionCommand, Result<SaveChapterScriptResult>>
{
    private readonly IPanelForgeDbContext _db;
    private readonly IWorkspaceAuthorizationService _auth;
    private readonly IPageRepository _pages;
    private readonly IChapterCastService _castService;
    private readonly IUnitOfWork _unitOfWork;

    public RestoreScriptVersionCommandHandler(
        IPanelForgeDbContext db,
        IWorkspaceAuthorizationService auth,
        IPageRepository pages,
        IChapterCastService castService,
        IUnitOfWork unitOfWork)
    {
        _db = db;
        _auth = auth;
        _pages = pages;
        _castService = castService;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<SaveChapterScriptResult>> Handle(RestoreScriptVersionCommand cmd, CancellationToken ct)
    {
        static Result<SaveChapterScriptResult> Fail(string msg, string code) => Result<SaveChapterScriptResult>.Failure(msg, code);

        // Lọc theo Chapter trên route (IDOR)
        var target = await _db.ScriptVersions.AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == cmd.VersionId && v.ChapterId == cmd.ChapterId, ct);
        if (target is null) return Fail("Phiên bản kịch bản không tồn tại trong chương này.", ResultErrorCodes.NotFound);

        var latest = await _db.ScriptVersions.AsNoTracking()
            .Where(v => v.ChapterId == cmd.ChapterId).OrderByDescending(v => v.VersionNumber).FirstAsync(ct);
        if (latest.ContentHash == target.ContentHash)
            return Fail("Kịch bản hiện tại đã giống phiên bản này, không có gì để khôi phục.", ResultErrorCodes.Validation);

        var pages = await _db.Pages.AsNoTracking().Where(p => p.ChapterId == cmd.ChapterId)
            .Select(p => new { p.Id, p.PageNumber }).ToListAsync(ct);
        var pageIds = pages.Select(p => p.Id).ToList();
        var livePanels = await _db.Panels.AsNoTracking().Where(p => pageIds.Contains(p.PageId))
            .Select(p => new { p.Id, p.PageId }).ToListAsync(ct);
        var liveLineIds = (await _db.ScriptLines.AsNoTracking()
            .Where(l => l.Scene.ChapterId == cmd.ChapterId).Select(l => l.Id).ToListAsync(ct)).ToHashSet();

        var pageIdByNumber = pages.ToDictionary(p => p.PageNumber, p => p.Id);
        var panelPage = livePanels.ToDictionary(p => p.Id, p => p.PageId);
        var warnings = new List<string>();
        IReadOnlyList<ScriptTreePage> tree;
        try
        {
            tree = ToTree(ScriptSnapshotBuilder.Deserialize(target.RowsJson),
                (panelId, pageNumber) => panelPage.TryGetValue(panelId, out var pageId) &&
                                         pageIdByNumber.GetValueOrDefault(pageNumber) == pageId,
                liveLineIds, warnings);
        }
        catch (FormatException ex)
        {
            return Fail(ex.Message, ResultErrorCodes.Validation);
        }

        try
        {
            return await _unitOfWork.ExecuteInTransactionAsync(async token =>
            {
                var outcome = await ChapterScriptSaver.SaveAsync(
                    _db, _auth, _pages, _castService, cmd.ChapterId, cmd.UserId,
                    new ChapterScriptBody(cmd.ExpectedRevisionId, tree), $"Khôi phục về phiên bản {target.VersionNumber}", token);
                return Result<SaveChapterScriptResult>.Success(new SaveChapterScriptResult(
                    outcome.Version.Id, outcome.Version.VersionNumber, outcome.Created, [.. warnings, .. outcome.Warnings]));
            }, ct);
        }
        catch (ScriptSaveAbortException ex)
        {
            return Result<SaveChapterScriptResult>.Failure(ex.Message, ex.Code, ex.Details);
        }
    }

    /// <summary>
    /// Ảnh chụp (thứ tự đọc) → cây lưu. Panel/dòng còn sống giữ Id để neo artwork, bóng thoại và tham chiếu Bible vẫn đúng;
    /// Panel đã xoá (hoặc nay ở trang khác) và dòng đã xoá mềm được tạo mới (Id dòng đã xoá không dùng lại được).
    /// Cast của panel đặt đúng như bản cũ ([] khi bản cũ không có). Dòng chưa neo Panel (dữ liệu cũ) bị bỏ kèm cảnh báo.
    /// </summary>
    internal static IReadOnlyList<ScriptTreePage> ToTree(
        IReadOnlyList<ScriptVersionRow> rows, Func<Guid, int, bool> panelStillOnPage, IReadOnlySet<Guid> liveLineIds,
        List<string> warnings)
    {
        static bool IsLine(ScriptVersionRow r) => r.Key.StartsWith("line:", StringComparison.Ordinal);

        ScriptTreeLine ToLine(ScriptVersionRow r)
        {
            if (!Enum.TryParse<ScriptLineKind>(r.Type, out var kind))
                throw new FormatException($"Phiên bản có dòng loại '{r.Type}' không đọc được nên không khôi phục được.");
            return new ScriptTreeLine(
                r.EntityId is { } id && liveLineIds.Contains(id) ? id : null,
                kind, r.Label, r.Text,
                r.Refs?.Select(x => new ScriptLineRefInput(x.SurfaceText, x.EntryId)).ToList());
        }

        var tree = rows.Where(r => r.Type == "page").Select(page => new ScriptTreePage(null, page.Page,
            rows.Where(r => r.Type == "panel" && r.Page == page.Page).Select(panel => new ScriptTreePanel(
                panel.EntityId is { } panelId && panelStillOnPage(panelId, page.Page) ? panelId : null,
                panel.Panel,
                string.IsNullOrEmpty(panel.Text) ? null : panel.Text,
                (panel.CastRevisions ?? []).Select(c => new ScriptTreeCast(c.EntryId, c.RevisionId)).ToList(),
                rows.Where(r => IsLine(r) && r.Page == page.Page && r.Panel == panel.Panel).Select(ToLine).ToList()))
            .ToList())).ToList();

        var unplaced = rows.Count(r => IsLine(r) && r.Page == 0);
        if (unplaced > 0)
            warnings.Add($"{unplaced} dòng của phiên bản cũ chưa thuộc panel nào nên không được khôi phục.");
        return tree;
    }
}
