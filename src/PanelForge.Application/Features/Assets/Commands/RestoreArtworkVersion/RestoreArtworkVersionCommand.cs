using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Assets.Common;
using PanelForge.Application.Features.Assets.Models;
using PanelForge.Application.Features.Assignments.Common;
using PanelForge.Application.Features.CanvasElements.Common;
using PanelForge.Domain.Entities.Assets;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Assets.Commands.RestoreArtworkVersion;

/// <summary>
/// ART-16 (Q27): Artist dùng lại một bản artwork cũ của (Page, Panel, Stage). Không sửa bản cũ, không tải lại file: tạo
/// AssetReference mới <c>VersionNumber = max + 1</c> trỏ cùng Asset (0 byte trên kho, append-only BR-01). Quyền giống upload.
/// Bản khôi phục là bản nháp như ảnh vừa upload: chỉ thành ảnh cuối sau lần duyệt Gate 2 kế tiếp (Q27, <c>FinalArtwork</c>).
/// </summary>
public sealed record RestoreArtworkVersionCommand(Guid ChapterId, Guid AssetReferenceId, Guid UserId)
    : IRequest<Result<AssetReferenceDto>>;

public sealed class RestoreArtworkVersionCommandHandler
    : IRequestHandler<RestoreArtworkVersionCommand, Result<AssetReferenceDto>>
{
    private readonly IPanelForgeDbContext _db;
    private readonly IPageRepository _pages;

    public RestoreArtworkVersionCommandHandler(IPanelForgeDbContext db, IPageRepository pages)
    {
        _db = db;
        _pages = pages;
    }

    public async Task<Result<AssetReferenceDto>> Handle(RestoreArtworkVersionCommand cmd, CancellationToken ct)
    {
        static Result<AssetReferenceDto> Fail(string msg, string code) => Result<AssetReferenceDto>.Failure(msg, code);

        // Lọc theo Chapter trên route để không khôi phục được ảnh của chương khác (IDOR)
        var source = await _db.AssetReferences.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == cmd.AssetReferenceId && r.ChapterId == cmd.ChapterId, ct);
        if (source is null)
            return Fail($"Bản artwork {cmd.AssetReferenceId} không thuộc chương này.", ResultErrorCodes.NotFound);

        var chapter = await _db.Chapters.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cmd.ChapterId, ct);
        if (chapter is null)
            return Fail($"Chapter {cmd.ChapterId} không tồn tại.", ResultErrorCodes.NotFound);
        if (AssignmentRules.ChapterNotEditableReason(chapter) is { } locked)
            return Fail(locked, ResultErrorCodes.Conflict);

        if (!await _db.Pages.AsNoTracking().AnyAsync(p => p.Id == source.PageId && p.ChapterId == chapter.Id, ct))
            return Fail("Trang của bản này không còn trong chương.", ResultErrorCodes.Conflict);
        if (source.PanelId is { } panelId && !await _db.Panels.AsNoTracking().AnyAsync(p => p.Id == panelId, ct))
            return Fail("Khung của bản này đã bị xoá khỏi bố cục trang, không khôi phục được.", ResultErrorCodes.Conflict);

        // Ô đã duyệt: phải được mở lại trước (Editor/Producer — REV-09), nếu không ảnh cuối đã duyệt bị thay lặng lẽ
        if (await _db.StageTasks.AsNoTracking().AnyAsync(t =>
                t.ChapterId == chapter.Id && t.PageId == source.PageId && t.PipelineStageId == source.PipelineStageId &&
                t.Status == StageTaskStatus.Approved, ct))
            return Fail("Trang đã được duyệt ở công đoạn này. Cần Editor/Producer mở lại trang trước khi khôi phục ảnh cũ.",
                ResultErrorCodes.Conflict);

        var workspaceId = await _db.Series.AsNoTracking()
            .Where(s => s.Id == chapter.SeriesId).Select(s => s.WorkspaceId).FirstOrDefaultAsync(ct);
        if (await ArtworkWriteRules.DeniedReasonAsync(_db, chapter, workspaceId, source.PipelineStageId, source.PageId,
                source.PanelId, cmd.UserId, ct) is { } denied)
            return Fail(denied, ResultErrorCodes.Forbidden);

        var latest = await _db.AssetReferences.AsNoTracking()
            .Where(r => r.PageId == source.PageId && r.PanelId == source.PanelId && r.PipelineStageId == source.PipelineStageId)
            .OrderByDescending(r => r.VersionNumber).FirstAsync(ct);
        if (latest.AssetId == source.AssetId)
            return Fail("Bản này đang là bản mới nhất, không cần khôi phục.", ResultErrorCodes.Conflict);

        var asset = await _db.Assets.AsNoTracking().FirstAsync(a => a.Id == source.AssetId, ct);
        var reference = AssetReference.Create(
            source.AssetId, source.WorkspaceId, chapter.Id, source.PageId, source.PipelineStageId,
            latest.VersionNumber + 1, source.LayerType, source.FileName, cmd.UserId, source.PanelId);
        reference.CreatedBy = cmd.UserId.ToString();
        _db.AssetReferences.Add(reference);

        if (await ArtworkWriteRules.IsLaterArtStageAsync(_db, chapter.SeriesId, source.PipelineStageId, ct))
            await BalloonFitRules.FlagAsync(_db, _pages, source.PageId, source.PanelId, ct);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // BR-11: upload/khôi phục cùng lúc vào cùng chỗ tính trùng VersionNumber (unique index)
            return Fail("Có người vừa tải ảnh lên cùng chỗ này. Tải lại rồi thử lại.", ResultErrorCodes.Conflict);
        }

        return Result<AssetReferenceDto>.Success(AssetReferenceDto.From(reference, asset, reused: true));
    }
}
