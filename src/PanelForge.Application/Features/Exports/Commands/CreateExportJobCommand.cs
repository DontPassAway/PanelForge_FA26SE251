using MediatR;
using PanelForge.AI.Contracts;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.AiAgents.Common;
using PanelForge.Application.Features.Chapters.Common;
using PanelForge.Application.Features.Exports.Common;
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
        var openJson = preset.Code == OpenJsonExport.Format;
        if (!openJson && !_renderer.Supports(preset.Code))
            return Fail($"Định dạng {preset.Code} chưa được hỗ trợ xuất.", ResultErrorCodes.Validation);

        var chapter = await _db.Chapters.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cmd.ChapterId, ct);
        if (chapter is null) return Fail($"Chapter {cmd.ChapterId} không tồn tại.", ResultErrorCodes.NotFound);
        if (chapter.Status is not (ChapterStatus.ChapterApproved or ChapterStatus.HandedOffAndPublished))
            return Fail("Chỉ xuất được chương đã duyệt (Approved) hoặc đã phát hành.", ResultErrorCodes.Conflict);

        // CF4 / BR-01: bản xuất lấy đúng artwork đã chốt trong ChapterSnapshot lúc duyệt Gate 3, không chọn lại từ ảnh hiện tại
        // (ảnh tải lên sau lúc duyệt không lọt vào file). Chương duyệt trước CF4 chưa có snapshot thì mới chọn theo FinalArtwork.
        var snapshot = await OpenJsonExport.SnapshotOfAsync(_db, chapter.Id, chapter.VersionVector, ct);
        if (snapshot is not null && AiContentValidator.Sha256(snapshot.ManifestJson) != snapshot.ManifestHash)
            return Fail("Manifest của bản duyệt không khớp hash đã lưu, không xuất được.", ResultErrorCodes.Conflict);
        // CF4 4.6: JSON mang manifest của bản duyệt nên phải có snapshot
        if (openJson && snapshot is null)
            return Fail("Chương chưa có bản duyệt (snapshot) nên chưa xuất JSON được.", ResultErrorCodes.Conflict);

        var pages = snapshot is null
            ? await LiveArtworkAsync(chapter.Id, ct)
            : SnapshotArtwork(snapshot.ManifestJson);
        var missingNumbers = pages.Where(p => p.RefIds.Count == 0).Select(p => p.PageNumber).ToList();
        if (pages.Count == 0 || missingNumbers.Count > 0)
            return Fail(missingNumbers.Count > 0
                ? $"Trang {string.Join(", ", missingNumbers)} chưa có artwork (cả trang hoặc theo khung) ở bản đã duyệt, chưa xuất được."
                : "Chương chưa có trang nào có artwork.", ResultErrorCodes.Conflict);

        var workspaceId = await _db.Series.AsNoTracking().Where(s => s.Id == chapter.SeriesId).Select(s => s.WorkspaceId).FirstAsync(ct);
        var job = ExportJob.Create(chapter.Id, workspaceId, preset.Id, preset.Code, cmd.UserId, chapter.VersionVector,
            pages.SelectMany(p => p.RefIds));
        job.CreatedBy = cmd.UserId.ToString();
        _db.ExportJobs.Add(job);
        await _db.SaveChangesAsync(ct);

        return Result<ExportJobDto>.Success(ExportJobDto.From(job, pages.Count));
    }

    private sealed record PageArtwork(int PageNumber, IReadOnlyList<Guid> RefIds);

    private static List<PageArtwork> SnapshotArtwork(string manifestJson) =>
        (AiJson.Deserialize<SnapshotManifest>(manifestJson)?.Pages ?? [])
        .OrderBy(p => p.PageNumber)
        .Select(p => new PageArtwork(p.PageNumber, p.ArtworkReferenceId is { } whole
            ? [whole]
            : p.PanelArtworks?.Select(a => a.ArtworkReferenceId).ToList() ?? []))
        .ToList();

    private async Task<List<PageArtwork>> LiveArtworkAsync(Guid chapterId, CancellationToken ct)
    {
        var artwork = await FinalArtwork.SelectAsync(_db, chapterId, ct);
        var pages = await _db.Pages.AsNoTracking().Where(p => p.ChapterId == chapterId)
            .Select(p => new { p.Id, p.PageNumber }).ToListAsync(ct);
        return pages.OrderBy(p => p.PageNumber)
            .Select(p => new PageArtwork(p.PageNumber,
                artwork.FirstOrDefault(a => a.PageId == p.Id)?.AssetReferenceIds.ToList() ?? []))
            .ToList();
    }
}
