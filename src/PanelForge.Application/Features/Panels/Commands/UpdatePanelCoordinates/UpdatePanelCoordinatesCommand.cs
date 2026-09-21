using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Panels.Models;
using PanelForge.Application.Interfaces.Persistence;

namespace PanelForge.Application.Features.Panels.Commands.UpdatePanelCoordinates;

public sealed record UpdatePanelCoordinatesCommand(
    Guid PanelId,
    Guid UserId,
    string BoundingBox,
    int? ReadingOrder = null
) : IRequest<Result<PanelDto>>;

public sealed class UpdatePanelCoordinatesCommandHandler : IRequestHandler<UpdatePanelCoordinatesCommand, Result<PanelDto>>
{
    private readonly IPanelForgeDbContext _dbContext;

    public UpdatePanelCoordinatesCommandHandler(IPanelForgeDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<PanelDto>> Handle(UpdatePanelCoordinatesCommand command, CancellationToken cancellationToken)
    {
        var panel = await _dbContext.Panels
            .Include(p => p.Elements)
            .FirstOrDefaultAsync(p => p.Id == command.PanelId, cancellationToken);

        if (panel == null)
            return Result<PanelDto>.Failure($"Panel {command.PanelId} không tồn tại.");

        try
        {
            panel.UpdateBoundingBox(command.BoundingBox);
            if (command.ReadingOrder.HasValue && command.ReadingOrder.Value > 0)
            {
                panel.UpdateReadingOrder(command.ReadingOrder.Value);
            }
            panel.UpdatedBy = command.UserId.ToString();
        }
        catch (ArgumentException ex)
        {
            return Result<PanelDto>.Failure(ex.Message);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result<PanelDto>.Success(new PanelDto(
            Id: panel.Id,
            PageId: panel.PageId,
            PanelNumber: panel.PanelNumber,
            BoundingBox: panel.BoundingBox,
            ReadingOrder: panel.ReadingOrder,
            CurrentStageId: panel.CurrentStageId,
            CreatedAt: panel.CreatedAt,
            UpdatedAt: panel.UpdatedAt,
            CreatedBy: panel.CreatedBy,
            ElementsCount: panel.Elements.Count
        ));
    }
}
