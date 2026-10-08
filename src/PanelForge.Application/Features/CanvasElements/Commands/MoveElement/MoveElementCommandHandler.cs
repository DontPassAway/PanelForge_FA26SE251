using MediatR;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Services;

namespace PanelForge.Application.Features.CanvasElements.Commands.MoveElement;

public sealed class MoveElementCommandHandler
    : IRequestHandler<MoveElementCommand, Result<bool>>
{
    private readonly IPageRepository _pageRepository;

    public MoveElementCommandHandler(IPageRepository pageRepository)
    {
        _pageRepository = pageRepository;
    }

    public async Task<Result<bool>> Handle(
        MoveElementCommand command,
        CancellationToken cancellationToken)
    {
        // 1. Load Page aggregate (Marten replay event stream)
        var page = await _pageRepository.GetAsync(command.PageId, cancellationToken);
        if (page is null)
            return Result<bool>.Failure($"Page {command.PageId} không tồn tại.");

        // 2. Optimistic Concurrency (BR-11): theo element nếu client gửi, nếu không thì theo cả Page (client cũ)
        if (command.ExpectedElementVersion is null)
        {
            if (command.ExpectedPageVersion is null)
                return Result<bool>.Failure("Phải gửi expectedElementVersion (hoặc expectedPageVersion).");
            if (page.Version != command.ExpectedPageVersion)
                return Result<bool>.Failure(
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
            return Result<bool>.Failure(ex.Message);
        }

        // 4. Persist (Marten ném ConcurrentPageUpdateException nếu stream đổi giữa lúc load và lưu)
        try
        {
            await _pageRepository.SaveAsync(page, cancellationToken);
        }
        catch (ConcurrentPageUpdateException ex)
        {
            return Result<bool>.Failure(ex.Message);
        }

        return Result<bool>.Success(true);
    }
}
