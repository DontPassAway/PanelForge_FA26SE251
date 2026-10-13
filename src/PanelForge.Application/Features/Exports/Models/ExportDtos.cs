using PanelForge.Domain.Entities.Publishing;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Exports.Models;

public sealed record ExportJobDto(
    Guid Id,
    Guid ChapterId,
    string Format,
    ExportJobStatus Status,
    Guid RequestedByUserId,
    long ChapterVersion,
    int PageCount,
    string? FileName,
    long? SizeBytes,
    string? Sha256,
    string? Error,
    DateTime CreatedAt,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    int DownloadCount)
{
    public static ExportJobDto From(ExportJob j) => new(
        j.Id, j.ChapterId, j.Format, j.Status, j.RequestedByUserId, j.ChapterVersion, j.SnapshotArtworkIds.Count,
        j.ResultFileName, j.ResultSizeBytes, j.ResultSha256, j.Error, j.CreatedAt, j.StartedAt, j.CompletedAt, j.DownloadCount);
}

/// <summary>PresetCode: mã ExportPreset (CBZ, PDF, PAGE_IMAGES, WEBTOON).</summary>
public sealed record CreateExportJobRequest(string PresetCode);
