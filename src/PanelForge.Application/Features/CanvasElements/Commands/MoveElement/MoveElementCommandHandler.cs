using MediatR;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Chapters.Common;
using PanelForge.Application.Common.Interfaces.Services;

namespace PanelForge.Application.Features.CanvasElements.Commands.MoveElement;

public sealed class MoveElementCommandHandler
    : IRequestHandler<MoveElementCommand, Result<int>>
{
    private readonly IPageRepository _pageRepository;
    private readonly IPanelForgeDbContext _db;

    public MoveElementCommandHandler(IPageRepository pageRepository, IPanelForgeDbContext db)
    {
        _pageRepository = pageRepository;
        _db = db;
    }

    public async Task<Result<int>> Handle(
        MoveElementCommand command,
        CancellationToken cancellationToken)
    {
        // 1. Load Page aggregate (Marten replay event stream)
        var page = await _pageRepository.GetAsync(command.PageId, cancellationToken);
        if (page is null)
            return Result<int>.Failure($"Page {command.PageId} không tồn tại.");
        if (await ChapterEditGuard.ReadOnlyReasonAsync(_db, page.ChapterId, cancellationToken) is { } readOnly)
            return Result<int>.Failure(readOnly, ResultErrorCodes.ChapterReadOnly);

        // 2. Optimistic Concurrency (BR-11): theo element nếu client gửi, nếu không thì theo cả Page (client cũ)
        if (command.ExpectedElementVersion is null)
        {
            if (command.ExpectedPageVersion is null)
                return Result<int>.Failure("Phải gửi expectedElementVersion (hoặc expectedPageVersion).");
            if (page.Version != command.ExpectedPageVersion)
                return Result<int>.Failure(
                    $"Xung đột phiên bản: Page đã được cập nhật bởi người khác. " +
                    $"Vui lòng tải lại (expected={command.ExpectedPageVersion}, actual={page.Version}).");
        }

        // 3. Domain method — Page validate và raise ElementMovedEvent
        try
        {
            page.EnsureElementVersion(command.ElementId, command.ExpectedElementVersion);
            page.MoveElement(
                elementId:     command.ElementId,
                newX:          command.NewX,
                newY:          command.NewY,
                newWidth:      command.NewWidth,
                newHeight:     command.NewHeight,
                movedByUserId: command.MovedByUserId,
                newRotation:   command.NewRotation,
                newZIndex:     command.NewZIndex
            );
        }
        catch (InvalidOperationException ex)
        {
            return Result<int>.Failure(ex.Message);
        }

        // 4. Persist (Marten ném ConcurrentPageUpdateException nếu stream đổi giữa lúc load và lưu)
        try
        {
            await _pageRepository.SaveAsync(page, cancellationToken);
        }
        catch (ConcurrentPageUpdateException ex)
        {
            return Result<int>.Failure(ex.Message);
        }

        // Phiên bản thật sau khi lưu (thao tác không đổi gì thì không tăng) — client dùng số này, không tự +1
        return Result<int>.Success(page.Elements[command.ElementId].Version);
    }
}
