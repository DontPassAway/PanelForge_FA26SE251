using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.Chapters.Common;
using PanelForge.Application.Features.Exports.Models;
using PanelForge.Domain.Entities.Publishing;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Exports.Commands;

/// <summary>
/// UC-12 (CF5 bước 8): Producer xuất chương theo ExportPreset. Chỉ chương Approved/Published (không xuất trước khi duyệt) và
/// mọi trang phải có ảnh cả trang ở bản đã duyệt. Job chụp artwork theo thứ tự trang rồi xếp hàng cho worker (UC-14).
/// </summary>
public sealed record CreateExportJobCommand(Guid ChapterId, Guid UserId, string? PresetCode) : IRequest<Result<ExportJobDto>>;

public sealed class CreateExportJobCommandHandler : IRequestHandler<CreateExportJobCommand, Result<ExportJobDto>>
{
    private readonly IPanelForgeDbContext _db;
    private readonly IChapterExportRenderer _renderer;

    public CreateExportJobCommandHandler(IPanelForgeDbContext db, IChapterExportRenderer renderer)
    {
        _db = db;
        _renderer = renderer;
    }

    public async Task<Result<ExportJobDto>> Handle(CreateExportJobCommand cmd, CancellationToken ct)
    {
        static Result<ExportJobDto> Fail(string m, string c) => Result<ExportJobDto>.Failure(m, c);

        var code = cmd.PresetCode?.Trim().ToUpperInvariant();
        var preset = string.IsNullOrEmpty(code) ? null
            : await _db.ExportPresets.AsNoTracking().FirstOrDefaultAsync(p => p.Code == code && p.IsActive, ct);
        if (preset is null) return Fail($"Không có preset xuất '{cmd.PresetCode}' đang dùng.", ResultErrorCodes.Validation);
        if (!_renderer.Supports(preset.Code))
            return Fail($"Định dạng {preset.Code} chưa được hỗ trợ xuất.", ResultErrorCodes.Validation);

        var chapter = await _db.Chapters.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cmd.ChapterId, ct);
        if (chapter is null) return Fail($"Chapter {cmd.ChapterId} không tồn tại.", ResultErrorCodes.NotFound);
        if (chapter.Status is not (ChapterStatus.Approved or ChapterStatus.Published))
            return Fail("Chỉ xuất được chương đã duyệt (Approved) hoặc đã phát hành.", ResultErrorCodes.Conflict);

        var artwork = await FinalArtwork.SelectAsync(_db, chapter.Id, ct);
        var missing = await _db.Pages.AsNoTracking()
            .Where(p => p.ChapterId == chapter.Id).Select(p => new { p.Id, p.PageNumber }).ToListAsync(ct);
        var missingNumbers = missing.Where(p => artwork.All(a => a.PageId != p.Id)).Select(p => p.PageNumber).OrderBy(n => n).ToList();
        if (artwork.Count == 0 || missingNumbers.Count > 0)
            return Fail(missingNumbers.Count > 0
                ? $"Trang {string.Join(", ", missingNumbers)} chưa có ảnh cả trang ở bản đã duyệt, chưa xuất được."
                : "Chương chưa có trang nào có artwork.", ResultErrorCodes.Conflict);

        var workspaceId = await _db.Series.AsNoTracking().Where(s => s.Id == chapter.SeriesId).Select(s => s.WorkspaceId).FirstAsync(ct);
        var job = ExportJob.Create(chapter.Id, workspaceId, preset.Id, preset.Code, cmd.UserId, chapter.VersionVector,
            artwork.Select(a => a.AssetReferenceId));
        job.CreatedBy = cmd.UserId.ToString();
        _db.ExportJobs.Add(job);
        await _db.SaveChangesAsync(ct);

        return Result<ExportJobDto>.Success(ExportJobDto.From(job));
    }
}
