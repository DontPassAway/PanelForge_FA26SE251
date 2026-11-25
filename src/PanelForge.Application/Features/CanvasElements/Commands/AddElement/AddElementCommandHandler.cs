using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Chapters.Common;
using PanelForge.Application.Features.CanvasElements.Common;
using PanelForge.Domain.Events;
using PanelForge.Application.Features.Assignments.Common;

namespace PanelForge.Application.Features.CanvasElements.Commands.AddElement;

public sealed class AddElementCommandHandler
    : IRequestHandler<AddElementCommand, Result<AddElementResponse>>
{
    private readonly IPageRepository _pageRepository;
    private readonly IPanelForgeDbContext _db;

    public AddElementCommandHandler(IPageRepository pageRepository, IPanelForgeDbContext db)
    {
        _pageRepository = pageRepository;
        _db = db;
    }

    public async Task<Result<AddElementResponse>> Handle(
        AddElementCommand command,
        CancellationToken cancellationToken)
    {
        // 1. Load Page aggregate từ Event Store (Marten replay toàn bộ event stream)
        var page = await _pageRepository.GetAsync(command.PageId, cancellationToken);
        if (page is null)
            return Result<AddElementResponse>.Failure(
                $"Page {command.PageId} không tồn tại.");
        if (await ChapterEditGuard.ReadOnlyReasonAsync(_db, page.ChapterId, cancellationToken) is { } readOnly)
            return Result<AddElementResponse>.Failure(readOnly, ResultErrorCodes.ChapterReadOnly);
        // Chỉ người giữ việc đang làm của phần này mới ghi (không đè phần đã chuyển giao cho người khác)
        if (await WorkScopeGuard.DenyReasonAsync(_db, command.AddedByUserId, page.Id, command.PanelId, cancellationToken) is { } outOfScope)
            return Result<AddElementResponse>.Failure(outOfScope, ResultErrorCodes.Forbidden);

        // NFR-09: gửi lại sau mất mạng → trả element đã tạo từ lần trước (idempotent), không thêm bản trùng
        if (command.ClientElementId is { } clientId && page.Elements.TryGetValue(clientId, out var existing))
            return existing.ElementType == command.ElementType
                ? Result<AddElementResponse>.Success(new AddElementResponse(
                    clientId, command.PageId, existing.ElementType, existing.ScriptLineId, existing.SpeakerId, Replayed: true))
                : Result<AddElementResponse>.Failure(
                    $"Xung đột: clientElementId {clientId} đã dùng cho element loại {existing.ElementType} trên Page này.");

        // 2. BR-05: nếu gắn ScriptLine thì dòng phải tồn tại, cùng Chapter với Page; speaker lấy từ dòng (không tin client)
        string? content = command.Content;
        Guid? speakerId = null;
        string? sourceLineText = null;

        if (command.ScriptLineId is { } lineId)
        {
            var (line, error) = await LetteringPageAnalysis.ResolveLineAsync(_db, lineId, page, command.ElementType, cancellationToken);
            if (line is null) return Result<AddElementResponse>.Failure(error!);

            sourceLineText = line.Text;
            speakerId = line.Speaker;
            // Không gửi Content = dùng nguyên văn dòng; gửi Content = Letterer đã rút gọn cho vừa balloon
            content = string.IsNullOrWhiteSpace(content) ? sourceLineText : content;
        }

        // Panel phải thuộc Page; mục Bible phải thuộc Series của Page (không tin client)
        if (command.PanelId is { } panelId &&
            !await _db.Panels.AsNoTracking().AnyAsync(p => p.Id == panelId && p.PageId == page.Id, cancellationToken))
            return Result<AddElementResponse>.Failure($"Panel {panelId} không thuộc Page {page.Id}.");

        if (command.BibleEntryId is { } entryId)
        {
            var seriesId = await _db.Chapters.AsNoTracking()
                .Where(c => c.Id == page.ChapterId).Select(c => c.SeriesId).FirstOrDefaultAsync(cancellationToken);
            var inSeries = await _db.BibleEntries.AsNoTracking().AnyAsync(e => e.Id == entryId &&
                _db.SeriesBibles.Any(b => b.Id == e.SeriesBibleId && b.SeriesId == seriesId), cancellationToken);
            if (!inSeries)
                return Result<AddElementResponse>.Failure($"Mục Series Bible {entryId} không thuộc Series của Page.");
        }

        // 3. Gọi domain method — Page validate (kể cả unique ScriptLine) và raise ElementAddedEvent
        try
        {
            page.AddElement(
                elementType:    command.ElementType,
                x:              command.X,
                y:              command.Y,
                width:          command.Width,
                height:         command.Height,
                zIndex:         command.ZIndex,
                addedByUserId:  command.AddedByUserId,
                content:        content,
                assetId:        command.AssetId,
                scriptLineId:   command.ScriptLineId,
                speakerId:      speakerId,
                sourceLineText: sourceLineText,
                elementId:      command.ClientElementId,
                panelId:        command.PanelId,
                rotation:       command.Rotation,
                bibleEntryId:   command.BibleEntryId
            );
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return Result<AddElementResponse>.Failure(ex.Message);
        }

        // 4. Lấy ElementId từ event vừa raise
        var elementId = page.UncommittedEvents.Last() is ElementAddedEvent added
            ? added.ElementId
            : Guid.Empty;

        // 5. Persist: Marten APPEND ElementAddedEvent vào stream (không INSERT row elements).
        //    Stream đã đổi từ lúc load (request song song) → ConcurrentPageUpdateException, trả lỗi xung đột thay vì 500.
        try { await _pageRepository.SaveAsync(page, cancellationToken); }
        catch (ConcurrentPageUpdateException ex) { return Result<AddElementResponse>.Failure(ex.Message); }

        return Result<AddElementResponse>.Success(new AddElementResponse(
            ElementId:    elementId,
            PageId:       command.PageId,
            ElementType:  command.ElementType,
            ScriptLineId: command.ScriptLineId,
            SpeakerId:    speakerId
        ));
    }
}
