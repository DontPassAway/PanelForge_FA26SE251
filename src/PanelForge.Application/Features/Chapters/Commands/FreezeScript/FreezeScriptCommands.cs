using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Chapters.Models;
using PanelForge.Application.Features.Chapters.Queries.CheckChapterBibleGate;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Chapters.Commands.FreezeScript;

/// <summary>
/// V2.5: Producer duyệt kịch bản thuần túy và bắt đầu sản xuất (Freeze Script & Hand-off Gate).
/// Đóng băng kịch bản chỉ đọc đối với Writer, kích hoạt giai đoạn sản xuất tranh (Thumbnail/Layout).
/// </summary>
public sealed record FreezeChapterScriptCommand(Guid ChapterId, Guid UserId) : IRequest<Result<ChapterDetailDto>>;

public sealed class FreezeChapterScriptCommandHandler : IRequestHandler<FreezeChapterScriptCommand, Result<ChapterDetailDto>>
{
    private readonly IPanelForgeDbContext _dbContext;
    private readonly IMediator _mediator;

    public FreezeChapterScriptCommandHandler(IPanelForgeDbContext dbContext, IMediator mediator)
    {
        _dbContext = dbContext;
        _mediator = mediator;
    }

    public async Task<Result<ChapterDetailDto>> Handle(FreezeChapterScriptCommand cmd, CancellationToken ct)
    {
        var chapter = await _dbContext.Chapters
            .Include(c => c.Scenes)
            .Include(c => c.Pages)
            .FirstOrDefaultAsync(c => c.Id == cmd.ChapterId, ct);

        if (chapter is null)
            return Result<ChapterDetailDto>.Failure($"Chương {cmd.ChapterId} không tồn tại.", ResultErrorCodes.NotFound);

        if (chapter.Status != ChapterStatus.InProduction)
            return Result<ChapterDetailDto>.Failure($"Chỉ duyệt kịch bản được cho chương đang trong sản xuất (InProduction), hiện là {chapter.Status}.", ResultErrorCodes.Conflict);

        if (chapter.IsScriptFrozen)
            return Result<ChapterDetailDto>.Failure("Kịch bản của chương này đã được duyệt và đóng băng trước đó.", ResultErrorCodes.Conflict);

        // 1. Kiểm tra kịch bản có nội dung
        var sceneIds = chapter.Scenes.Select(s => s.Id).ToList();
        var scriptLineCount = await _dbContext.ScriptLines
            .AsNoTracking()
            .CountAsync(l => sceneIds.Contains(l.SceneId), ct);

        if (chapter.Scenes.Count == 0 || scriptLineCount == 0)
            return Result<ChapterDetailDto>.Failure("Kịch bản chương phải có ít nhất một phân cảnh và một dòng kịch bản trước khi duyệt đưa vào sản xuất.", ResultErrorCodes.Validation);

        // 2. Kiểm tra Chapter-Scoped Bible Gate
        var gateResult = await _mediator.Send(new CheckChapterBibleGateQuery(chapter.Id), ct);
        if (!gateResult.IsSuccess)
            return Result<ChapterDetailDto>.Failure(gateResult.ErrorMessage ?? "Lỗi kiểm tra Chapter Bible Gate.", gateResult.ErrorCode);

        var gate = gateResult.Value;
        if (gate is null || !gate.IsPassed)
        {
            var pendingNames = gate is not null
                ? string.Join(", ", gate.Items.Where(i => !i.IsLocked).Select(i => $"{i.Name} ({i.Code}: {i.WarningCode})"))
                : "Không xác định";
            var pendingCount = gate?.PendingCount ?? 0;
            return Result<ChapterDetailDto>.Failure(
                $"Rào chắn Series Bible theo Chương (Chapter-Scoped Bible Gate) chưa đạt: Còn {pendingCount} mục Bible chưa có bản CanonLocked hiệu lực tại Chương {chapter.ChapterNumber} [{pendingNames}]. Vui lòng hoàn tất duyệt khóa Series Bible trước khi bắt đầu sản xuất.",
                ResultErrorCodes.Conflict);
        }

        // 3. Đóng băng kịch bản
        chapter.FreezeScript(cmd.UserId);
        chapter.UpdatedBy = cmd.UserId.ToString();

        await _dbContext.SaveChangesAsync(ct);

        var scenes = chapter.Scenes.OrderBy(s => s.SceneNumber).Select(s => new ChapterSceneSummaryDto(
            s.Id, s.SceneNumber, s.Heading, s.Summary)).ToList();
        var pages = chapter.Pages.OrderBy(p => p.PageNumber).Select(p => new ChapterPageSummaryDto(
            p.Id, p.PageNumber, p.LayoutFormat.ToString(), p.WidthPx, p.HeightPx, p.Dpi)).ToList();

        return Result<ChapterDetailDto>.Success(new ChapterDetailDto(
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
            ScriptFrozenById: chapter.ScriptFrozenById
        ));
    }
}

/// <summary>
/// V2.5: Mở lại kịch bản (Unfreeze Script) khi cần sửa đổi kịch bản trong quá trình sản xuất.
/// </summary>
public sealed record UnfreezeChapterScriptCommand(Guid ChapterId, Guid UserId, string? Reason = null) : IRequest<Result<ChapterDetailDto>>;

public sealed class UnfreezeChapterScriptCommandHandler : IRequestHandler<UnfreezeChapterScriptCommand, Result<ChapterDetailDto>>
{
    private readonly IPanelForgeDbContext _dbContext;

    public UnfreezeChapterScriptCommandHandler(IPanelForgeDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<ChapterDetailDto>> Handle(UnfreezeChapterScriptCommand cmd, CancellationToken ct)
    {
        var chapter = await _dbContext.Chapters
            .Include(c => c.Scenes)
            .Include(c => c.Pages)
            .FirstOrDefaultAsync(c => c.Id == cmd.ChapterId, ct);

        if (chapter is null)
            return Result<ChapterDetailDto>.Failure($"Chương {cmd.ChapterId} không tồn tại.", ResultErrorCodes.NotFound);

        if (chapter.Status != ChapterStatus.InProduction)
            return Result<ChapterDetailDto>.Failure($"Chỉ mở lại kịch bản được cho chương đang trong sản xuất (InProduction), hiện là {chapter.Status}.", ResultErrorCodes.Conflict);

        if (!chapter.IsScriptFrozen)
            return Result<ChapterDetailDto>.Failure("Kịch bản của chương này chưa bị đóng băng.", ResultErrorCodes.Conflict);

        chapter.UnfreezeScript(cmd.UserId);
        chapter.UpdatedBy = cmd.UserId.ToString();

        await _dbContext.SaveChangesAsync(ct);

        var scenes = chapter.Scenes.OrderBy(s => s.SceneNumber).Select(s => new ChapterSceneSummaryDto(
            s.Id, s.SceneNumber, s.Heading, s.Summary)).ToList();
        var pages = chapter.Pages.OrderBy(p => p.PageNumber).Select(p => new ChapterPageSummaryDto(
            p.Id, p.PageNumber, p.LayoutFormat.ToString(), p.WidthPx, p.HeightPx, p.Dpi)).ToList();

        return Result<ChapterDetailDto>.Success(new ChapterDetailDto(
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
            ScriptFrozenById: chapter.ScriptFrozenById
        ));
    }
}
