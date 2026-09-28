using MediatR;
using PanelForge.Application.Common;
using PanelForge.Application.Interfaces;

namespace PanelForge.Application.Features.Elements.MoveElement;

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

        // 2. Optimistic Concurrency check — phát hiện concurrent edit
        if (page.Version != command.ExpectedPageVersion)
            return Result<bool>.Failure(
                $"Xung đột phiên bản: Page đã được cập nhật bởi người khác. " +
                $"Vui lòng tải lại (expected={command.ExpectedPageVersion}, actual={page.Version}).");

        // 3. Domain method — Page validate và raise ElementMovedEvent
        try
        {
            page.MoveElement(
                elementId:     command.ElementId,
                newX:          command.NewX,
                newY:          command.NewY,
                newWidth:      command.NewWidth,
                newHeight:     command.NewHeight,
                movedByUserId: command.MovedByUserId
            );
        }
        catch (InvalidOperationException ex)
        {
            return Result<bool>.Failure(ex.Message);
        }

        // 4. Persist (Marten sẽ throw nếu version conflict ở DB level)
        await _pageRepository.SaveAsync(page, cancellationToken);

        return Result<bool>.Success(true);
    }
}
