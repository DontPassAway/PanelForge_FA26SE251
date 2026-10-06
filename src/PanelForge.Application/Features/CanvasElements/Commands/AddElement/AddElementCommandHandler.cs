using MediatR;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common;
using PanelForge.Application.Features.CanvasElements.Common;
using PanelForge.Domain.Events;

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
                sourceLineText: sourceLineText
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
