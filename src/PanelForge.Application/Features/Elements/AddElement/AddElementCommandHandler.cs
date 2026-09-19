using MediatR;
using PanelForge.Application.Common;
using PanelForge.Application.Interfaces;
using PanelForge.Domain.Events;

namespace PanelForge.Application.Features.Elements.AddElement;

public sealed class AddElementCommandHandler
    : IRequestHandler<AddElementCommand, Result<AddElementResponse>>
{
    private readonly IPageRepository _pageRepository;

    public AddElementCommandHandler(IPageRepository pageRepository)
    {
        _pageRepository = pageRepository;
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

        // 2. Gọi domain method — Page validate và raise ElementAddedEvent
        try
        {
            page.AddElement(
                elementType:   command.ElementType,
                x:             command.X,
                y:             command.Y,
                width:         command.Width,
                height:        command.Height,
                zIndex:        command.ZIndex,
                addedByUserId: command.AddedByUserId,
                content:       command.Content,
                assetId:       command.AssetId
            );
        }
        catch (ArgumentException ex)
        {
            return Result<AddElementResponse>.Failure(ex.Message);
        }

        // 3. Lấy ElementId từ event vừa raise
        var elementId = page.UncommittedEvents.Last() is ElementAddedEvent added
            ? added.ElementId
            : Guid.Empty;

        // 4. Persist: Marten APPEND ElementAddedEvent vào stream (không INSERT row elements)
        await _pageRepository.SaveAsync(page, cancellationToken);

        return Result<AddElementResponse>.Success(new AddElementResponse(
            ElementId:   elementId,
            PageId:      command.PageId,
            ElementType: command.ElementType
        ));
    }
}
