using PanelForge.Application.Features.Chapters.Commands.SetChapterCover;
using PanelForge.Application.Features.Chapters.Models;
using PanelForge.Domain.Entities.Content;

namespace PanelForge.Application.Features.Chapters.Common;

internal static class ChapterDetailDtoFactory
{
    public static ChapterDetailDto Create(Chapter chapter)
    {
        var scenes = chapter.Scenes.OrderBy(s => s.SceneNumber).Select(s => new ChapterSceneSummaryDto(
            s.Id, s.SceneNumber, s.Heading, s.Summary)).ToList();
        var pages = chapter.Pages.OrderBy(p => p.PageNumber).Select(p => new ChapterPageSummaryDto(
            p.Id, p.PageNumber, p.LayoutFormat.ToString(), p.WidthPx, p.HeightPx, p.Dpi)).ToList();

        return new ChapterDetailDto(
            Id: chapter.Id,
            SeriesId: chapter.SeriesId,
            ChapterNumber: chapter.ChapterNumber,
            Title: chapter.Title,
            TargetReleaseDate: chapter.TargetReleaseDate,
            VersionVector: chapter.VersionVector,
            CreatedAt: chapter.CreatedAt,
            UpdatedAt: chapter.UpdatedAt,
            CreatedBy: chapter.CreatedBy,
            Scenes: scenes,
            Pages: pages,
            Status: chapter.Status,
            PublishedAt: chapter.PublishedAt,
            Outline: chapter.Outline,
            IsScriptFrozen: chapter.IsScriptFrozen,
            ScriptFrozenAt: chapter.ScriptFrozenAt,
            ScriptFrozenById: chapter.ScriptFrozenById,
            ScriptReopenStatus: chapter.ScriptReopenStatus,
            ScriptReopenReason: chapter.ScriptReopenReason,
            ScriptReopenRequestedById: chapter.ScriptReopenRequestedById,
            ScriptReopenRequestedAt: chapter.ScriptReopenRequestedAt,
            CoverUrl: SetChapterCoverCommandHandler.CoverUrl(chapter.Id, chapter.CoverImageSha256),
            ScriptContentHash: chapter.ScriptContentHash,
            ScriptStatus: chapter.ScriptStatus,
            ScriptSubmittedAt: chapter.ScriptSubmittedAt,
            LastSubmittedVersionId: chapter.LastSubmittedVersionId,
            PreviousSubmittedVersionId: chapter.PreviousSubmittedVersionId,
            IsHandedOff: chapter.IsHandedOff,
            HandedOffAt: chapter.HandedOffAt,
            HandedOffById: chapter.HandedOffById,
            FrozenScriptVersionId: chapter.FrozenScriptVersionId,
            PreviousFrozenScriptVersionId: chapter.PreviousFrozenScriptVersionId
        );
    }
}
