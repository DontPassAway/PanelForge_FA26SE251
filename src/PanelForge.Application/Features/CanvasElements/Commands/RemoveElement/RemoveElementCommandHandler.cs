using MediatR;
using PanelForge.Application.Common;
using PanelForge.Application.Interfaces;

namespace PanelForge.Application.Features.Elements.RemoveElement;

public sealed class RemoveElementCommandHandler
    : IRequestHandler<RemoveElementCommand, Result<bool>>
{
    private readonly IPageRepository _pageRepository;

    public RemoveElementCommandHandler(IPageRepository pageRepository)
    {
        _pageRepository = pageRepository;
    }

    public async Task<Result<bool>> Handle(
        RemoveElementCommand command,
        CancellationToken cancellationToken)
    {
        // 1. Load Page aggregate
        var page = await _pageRepository.GetAsync(command.PageId, cancellationToken);
        if (page is null)
            return Result<bool>.Failure($"Page {command.PageId} không tồn tại.");

        // 2. Domain method — validate element tồn tại và chưa bị xóa
        try
        {
            page.RemoveElement(
                elementId:       command.ElementId,
                reason:          command.Reason,
                removedByUserId: command.RemovedByUserId
            );
        }
        catch (InvalidOperationException ex)
        {
            return Result<bool>.Failure(ex.Message);
        }

        // 3. Persist ElementRemovedEvent — thao tác APPEND duy nhất, KHÔNG có DELETE SQL
        await _pageRepository.SaveAsync(page, cancellationToken);

        return Result<bool>.Success(true);
    }
}
