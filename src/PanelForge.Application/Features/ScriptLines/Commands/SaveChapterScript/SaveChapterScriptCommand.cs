using MediatR;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.Chapters.Services;
using PanelForge.Application.Features.ScriptLines.Models;

namespace PanelForge.Application.Features.ScriptLines.Commands.SaveChapterScript;

/// <summary>PUT /api/chapters/{id}/script: lưu cả kịch bản chương trong một request, một transaction (SCR-14, phương án 3B).</summary>
public sealed record SaveChapterScriptCommand(Guid ChapterId, Guid UserId, ChapterScriptBody Body, string? Message = null)
    : IRequest<Result<SaveChapterScriptResult>>;

public sealed class SaveChapterScriptCommandHandler : IRequestHandler<SaveChapterScriptCommand, Result<SaveChapterScriptResult>>
{
    private readonly IPanelForgeDbContext _db;
    private readonly IWorkspaceAuthorizationService _auth;
    private readonly IPageRepository _pages;
    private readonly IChapterCastService _castService;
    private readonly IUnitOfWork _unitOfWork;

    public SaveChapterScriptCommandHandler(
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

    public async Task<Result<SaveChapterScriptResult>> Handle(SaveChapterScriptCommand cmd, CancellationToken ct)
    {
        try
        {
            return await _unitOfWork.ExecuteInTransactionAsync(async token =>
            {
                var outcome = await ChapterScriptSaver.SaveAsync(
                    _db, _auth, _pages, _castService, cmd.ChapterId, cmd.UserId, cmd.Body, cmd.Message, token);
                return Result<SaveChapterScriptResult>.Success(
                    new SaveChapterScriptResult(outcome.Version.Id, outcome.Version.VersionNumber, outcome.Created, outcome.Warnings));
            }, ct);
        }
        catch (ScriptSaveAbortException ex)
        {
            return Result<SaveChapterScriptResult>.Failure(ex.Message, ex.Code);
        }
    }
}
