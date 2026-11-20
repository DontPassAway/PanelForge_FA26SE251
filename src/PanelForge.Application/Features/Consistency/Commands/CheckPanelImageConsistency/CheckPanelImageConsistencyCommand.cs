using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.Consistency.Models;

namespace PanelForge.Application.Features.Consistency.Commands.CheckPanelImageConsistency;

/// <summary>
/// So ảnh một Panel với character sheet đã khoá. Quyền Producer/Editor ở controller; chương chỉ đọc không ghi finding mới (BR-12).
/// </summary>
public sealed record CheckPanelImageConsistencyCommand(Guid PanelId, Guid UserId, string? RevisionRef, decimal? Threshold)
    : IRequest<Result<ImageConsistencyResultDto>>;

public sealed class CheckPanelImageConsistencyCommandHandler
    : IRequestHandler<CheckPanelImageConsistencyCommand, Result<ImageConsistencyResultDto>>
{
    private readonly IPanelForgeDbContext _db;
    private readonly IImageConsistencyService _vision;

    public CheckPanelImageConsistencyCommandHandler(IPanelForgeDbContext db, IImageConsistencyService vision)
    {
        _db = db;
        _vision = vision;
    }

    public async Task<Result<ImageConsistencyResultDto>> Handle(CheckPanelImageConsistencyCommand cmd, CancellationToken ct)
    {
        var chapter = await (from panel in _db.Panels.AsNoTracking()
                             join page in _db.Pages.AsNoTracking() on panel.PageId equals page.Id
                             join c in _db.Chapters.AsNoTracking() on page.ChapterId equals c.Id
                             where panel.Id == cmd.PanelId
                             select c).FirstOrDefaultAsync(ct);
        if (chapter is null)
            return Result<ImageConsistencyResultDto>.Failure($"Panel {cmd.PanelId} không tồn tại.", ResultErrorCodes.NotFound);
        if (!chapter.IsContentEditable)
            return Result<ImageConsistencyResultDto>.Failure(
                $"Chapter đang ở trạng thái {chapter.Status} (chỉ đọc), không chạy kiểm tra mới.", ResultErrorCodes.Conflict);

        try
        {
            return Result<ImageConsistencyResultDto>.Success(
                await _vision.CheckPanelConsistencyAsync(cmd.PanelId, cmd.RevisionRef, cmd.Threshold, cmd.UserId, ct));
        }
        catch (KeyNotFoundException ex)
        {
            return Result<ImageConsistencyResultDto>.Failure(ex.Message, ResultErrorCodes.NotFound);
        }
        catch (InvalidOperationException ex)
        {
            return Result<ImageConsistencyResultDto>.Failure(ex.Message, ResultErrorCodes.Validation);
        }
    }
}
