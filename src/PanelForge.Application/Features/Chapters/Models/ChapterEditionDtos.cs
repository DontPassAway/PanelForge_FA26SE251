using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Chapters.Models;

public sealed record ChapterEditionDto(
    Guid Id,
    Guid ChapterId,
    string LanguageCode,
    EditionStatus Status,
    bool IsPaid,
    decimal PriceVnd,
    int FreePreviewPages,
    Guid? CurrentSnapshotId,
    Guid? TranslatorUserId,
    string? TranslatorName,
    DateTime? ApprovedAt,
    Guid? ApprovedById,
    string? ApprovedByName,
    DateTime? PublishedAt,
    long Version,
    DateTime CreatedAt,
    DateTime? UpdatedAt
);

public sealed record SubmitEditionTranslationRequest();

public sealed record ReviewEditionRequest(
    ReviewEditionAction Action,
    string? Comment = null
);

public enum ReviewEditionAction
{
    Approve = 1,
    RequestChange = 2
}

public sealed record ReopenEditionRequest(
    string Reason
);

public static class ChapterEditionMapping
{
    public static ChapterEditionDto ToDto(ChapterEdition e, string? translatorName = null, string? approvedByName = null) =>
        new(
            Id: e.Id,
            ChapterId: e.ChapterId,
            LanguageCode: e.LanguageCode,
            Status: e.Status,
            IsPaid: e.IsPaid,
            PriceVnd: e.PriceVnd,
            FreePreviewPages: e.FreePreviewPages,
            CurrentSnapshotId: e.CurrentSnapshotId,
            TranslatorUserId: e.TranslatorUserId,
            TranslatorName: translatorName ?? e.TranslatorUser?.FullName,
            ApprovedAt: e.ApprovedAt,
            ApprovedById: e.ApprovedById,
            ApprovedByName: approvedByName,
            PublishedAt: e.PublishedAt,
            Version: e.Version,
            CreatedAt: e.CreatedAt,
            UpdatedAt: e.UpdatedAt
        );
}
