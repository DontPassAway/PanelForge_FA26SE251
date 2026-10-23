using System.Text;
using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.AI.Contracts;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.AiAgents.Common;
using PanelForge.Application.Features.AiAgents.Queries.GetProvenanceReport;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Entities.Publishing;

namespace PanelForge.Application.Features.Exports.Common;

/// <summary>
/// CF4 4.6 / CF5 5.3b (NFR-04, NFR-10, BR-10, BR-18): file JSON mở của chương = manifest <see cref="ChapterSnapshot"/> của bản đã
/// duyệt + Provenance Report (dùng lại <see cref="GetProvenanceReportQuery"/>, không tổng hợp lại). Kiểm chứng ngoài nền tảng:
/// <c>snapshot.manifestHash</c> = SHA-256 (hex thường) của đúng chuỗi UTF-8 <c>snapshot.manifestJson</c>; SHA-256 của cả file
/// lưu ở <c>ExportJob.ResultSha256</c> (trả trong danh sách bản xuất).
/// </summary>
internal static class OpenJsonExport
{
    public const string Format = "OPEN_JSON";
    public const string Schema = "panelforge.open-json";
    public const string SchemaVersion = "1.0";

    private static readonly JsonSerializerOptions Indented = new(AiJson.Options) { WriteIndented = true };

    /// <summary>Snapshot của đúng bản đã duyệt mà job chụp (cùng Chapter.VersionVector); chương duyệt trước CF4 thì lấy bản mới nhất.</summary>
    public static async Task<ChapterSnapshot?> SnapshotOfAsync(
        IPanelForgeDbContext db, Guid chapterId, long chapterVersion, CancellationToken ct) =>
        await db.ChapterSnapshots.AsNoTracking()
            .Where(s => s.ChapterId == chapterId && s.ChapterVersion == chapterVersion)
            .OrderByDescending(s => s.SnapshotNumber).FirstOrDefaultAsync(ct)
        ?? await db.ChapterSnapshots.AsNoTracking()
            .Where(s => s.ChapterId == chapterId)
            .OrderByDescending(s => s.SnapshotNumber).FirstOrDefaultAsync(ct);

    /// <summary>Lỗi dữ liệu ném <see cref="InvalidOperationException"/> để worker đánh dấu job Failed kèm lý do.</summary>
    public static async Task<(string FileName, ExportFile File)> BuildAsync(
        IPanelForgeDbContext db, ISender sender, ExportJob job, CancellationToken ct)
    {
        var info = await db.Chapters.AsNoTracking().Where(c => c.Id == job.ChapterId)
            .Select(c => new { c.SeriesId, c.ChapterNumber, c.Title, SeriesTitle = c.Series.Title, c.Series.ReadingDirection })
            .FirstAsync(ct);

        var snapshot = await SnapshotOfAsync(db, job.ChapterId, job.ChapterVersion, ct)
                       ?? throw new InvalidOperationException("Chương chưa có bản duyệt (snapshot) nên chưa xuất JSON được.");
        if (AiContentValidator.Sha256(snapshot.ManifestJson) != snapshot.ManifestHash)
            throw new InvalidOperationException("Manifest của bản duyệt không khớp hash đã lưu, không xuất được.");

        // Quyền xem report (Producer/Editor) kiểm lại theo người yêu cầu tại lúc chạy job (BR-07)
        var report = await sender.Send(new GetProvenanceReportQuery(info.SeriesId, job.ChapterId, null, job.RequestedByUserId), ct);
        if (!report.IsSuccess)
            throw new InvalidOperationException($"Không dựng được Provenance Report: {report.ErrorMessage}");

        using var manifest = JsonDocument.Parse(snapshot.ManifestJson);
        var document = new
        {
            schema = Schema,
            schemaVersion = SchemaVersion,
            exportJobId = job.Id,
            exportedAtUtc = DateTime.UtcNow,
            series = new { id = info.SeriesId, title = info.SeriesTitle, readingDirection = info.ReadingDirection },
            chapter = new { id = job.ChapterId, chapterNumber = info.ChapterNumber, title = info.Title, chapterVersion = job.ChapterVersion },
            snapshot = new
            {
                id = snapshot.Id,
                snapshot.SnapshotNumber,
                snapshot.ChapterVersion,
                snapshot.CreatedByUserId,
                snapshot.CreatedAt,
                snapshot.ManifestHash,
                // Chuỗi gốc để tính lại hash (đọc lại thành object có thể đổi khoảng trắng/thứ tự); `manifest` để đọc
                snapshot.ManifestJson,
                manifest = manifest.RootElement
            },
            provenance = report.Value,
            verification = new
            {
                hashAlgorithm = "SHA-256",
                manifestHash = "SHA-256 (hex thường) của bytes UTF-8 của snapshot.manifestJson phải bằng snapshot.manifestHash.",
                fileHash = "SHA-256 của cả file phải bằng sha256 của bản xuất (GET /api/chapters/{chapterId}/exports)."
            }
        };

        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(document, Indented));
        return ($"{info.SeriesTitle} - Chuong {info.ChapterNumber} - provenance.json",
            new ExportFile(bytes, "application/json", "json"));
    }
}
