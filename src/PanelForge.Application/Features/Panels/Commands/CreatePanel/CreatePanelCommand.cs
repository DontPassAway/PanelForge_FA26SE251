using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Panels.Models;
using PanelForge.Application.Interfaces.Persistence;
using DomainPanel = PanelForge.Domain.Entities.Content.Panel;

namespace PanelForge.Application.Features.Panels.Commands.CreatePanel;

public sealed record CreatePanelCommand(
    Guid PageId,
    Guid UserId,
    int? PanelNumber = null,
    int? ReadingOrder = null,
    string? BoundingBox = null
) : IRequest<Result<PanelDto>>;

public sealed class CreatePanelCommandHandler : IRequestHandler<CreatePanelCommand, Result<PanelDto>>
{
    private readonly IPanelForgeDbContext _dbContext;

    public CreatePanelCommandHandler(IPanelForgeDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<PanelDto>> Handle(CreatePanelCommand command, CancellationToken cancellationToken)
    {
        // 1. Kiểm tra Page có tồn tại không
        var pageExists = await _dbContext.Pages
            .AnyAsync(p => p.Id == command.PageId, cancellationToken);

        if (!pageExists)
            return Result<PanelDto>.Failure($"Page {command.PageId} không tồn tại.");

        // 2. Tính số Panel và ReadingOrder tự động nếu chưa truyền vào
        int panelNumber;
        if (command.PanelNumber.HasValue && command.PanelNumber.Value > 0)
        {
            panelNumber = command.PanelNumber.Value;
        }
        else
        {
            var maxNum = await _dbContext.Panels
                .Where(p => p.PageId == command.PageId)
                .MaxAsync(p => (int?)p.PanelNumber, cancellationToken);
            panelNumber = (maxNum ?? 0) + 1;
        }

        int readingOrder;
        if (command.ReadingOrder.HasValue && command.ReadingOrder.Value > 0)
        {
            readingOrder = command.ReadingOrder.Value;
        }
        else
        {
            var maxOrder = await _dbContext.Panels
                .Where(p => p.PageId == command.PageId)
                .MaxAsync(p => (int?)p.ReadingOrder, cancellationToken);
            readingOrder = (maxOrder ?? 0) + 1;
        }

        // 3. Khởi tạo Panel
        DomainPanel panel;
        try
        {
            panel = DomainPanel.Create(
                pageId: command.PageId,
                panelNumber: panelNumber,
                readingOrder: readingOrder,
                boundingBox: command.BoundingBox ?? "{}"
            );
            panel.CreatedBy = command.UserId.ToString();
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return Result<PanelDto>.Failure(ex.Message);
        }

        _dbContext.Panels.Add(panel);
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
            ElementsCount: 0
        ));
    }
}
