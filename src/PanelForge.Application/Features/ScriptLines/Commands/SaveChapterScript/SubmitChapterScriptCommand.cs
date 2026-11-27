using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.Chapters.Common;
using PanelForge.Application.Features.Chapters.Models;
using PanelForge.Application.Features.Chapters.Services;
using PanelForge.Application.Features.ScriptLines.Models;

namespace PanelForge.Application.Features.ScriptLines.Commands.SaveChapterScript;

/// <summary>POST /api/chapters/{id}/script/submit: lưu cả kịch bản như SaveChapterScript rồi gửi duyệt, cùng một transaction.</summary>
public sealed record SubmitChapterScriptCommand(Guid ChapterId, Guid UserId, ChapterScriptBody Body, string? Message = null)
    : IRequest<Result<SubmitChapterScriptResult>>;

public sealed record SubmitChapterScriptResult(
    Guid RevisionId, int VersionNumber, bool Created, IReadOnlyList<string> Warnings, ChapterDetailDto Chapter);

public sealed class SubmitChapterScriptCommandHandler : IRequestHandler<SubmitChapterScriptCommand, Result<SubmitChapterScriptResult>>
{
    private readonly IPanelForgeDbContext _db;
    private readonly IWorkspaceAuthorizationService _auth;
    private readonly IPageRepository _pages;
    private readonly IChapterCastService _castService;
    private readonly IChapterCastGate _castGate;
    private readonly IUnitOfWork _unitOfWork;

    public SubmitChapterScriptCommandHandler(
        IPanelForgeDbContext db,
        IWorkspaceAuthorizationService auth,
        IPageRepository pages,
        IChapterCastService castService,
        IChapterCastGate castGate,
        IUnitOfWork unitOfWork)
    {
        _db = db;
        _auth = auth;
        _pages = pages;
        _castService = castService;
        _castGate = castGate;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<SubmitChapterScriptResult>> Handle(SubmitChapterScriptCommand cmd, CancellationToken ct)
    {
        try
        {
            return await _unitOfWork.ExecuteInTransactionAsync(async token =>
            {
                var outcome = await ChapterScriptSaver.SaveAsync(
                    _db, _auth, _pages, _castService, cmd.ChapterId, cmd.UserId, cmd.Body, cmd.Message, token);

                if (outcome.Version.LineCount == 0)
                    throw new ScriptSaveAbortException("Kịch bản còn trống, chưa gửi duyệt được.", ResultErrorCodes.Validation);

                var warnings = new List<string>(outcome.Warnings);
                var gate = await _castGate.EvaluateSubmitAsync(cmd.ChapterId, token);
                if (!gate.CanSubmit)
                {
                    var blockers = string.Join("; ", gate.Blockers.Select(b => $"{b.EntryName}: {b.Reason}"));
                    warnings.Add($"Series Bible chưa hoàn thành: {blockers}");
                }

                var chapter = await _db.Chapters
                    .Include(c => c.Scenes)
                    .Include(c => c.Pages)
                    .FirstAsync(c => c.Id == cmd.ChapterId, token);
                if (await ScriptSubmitRules.ApplySubmitAsync(_db, chapter, outcome.Version, cmd.UserId, outcome.WorkspaceId, token) is { } submitError)
                    throw new ScriptSaveAbortException(submitError, ResultErrorCodes.Conflict);

                await _db.SaveChangesAsync(token);
                return Result<SubmitChapterScriptResult>.Success(new SubmitChapterScriptResult(
                    outcome.Version.Id, outcome.Version.VersionNumber, outcome.Created, warnings,
                    ChapterDetailDtoFactory.Create(chapter)));
            }, ct);
        }
        catch (ScriptSaveAbortException ex)
        {
            return Result<SubmitChapterScriptResult>.Failure(ex.Message, ex.Code, ex.Details);
        }
    }
}
