using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Chapters.Common;
using PanelForge.Application.Features.Panels.Models;
using PanelForge.Application.Common.Interfaces.Persistence;

namespace PanelForge.Application.Features.Panels.Commands.UpdatePanelCoordinates;

public sealed record UpdatePanelCoordinatesCommand(
    Guid PanelId,
    Guid UserId,
    string BoundingBox,
    int? ReadingOrder = null,
    int? ExpectedVersion = null   // BR-11: version client đã thấy; null = không kiểm
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
            .FirstOrDefaultAsync(p => p.Id == command.PanelId, cancellationToken);

        if (panel == null)
            return Result<PanelDto>.Failure($"Panel {command.PanelId} không tồn tại.");
        if (await ChapterEditGuard.ReadOnlyReasonForPageAsync(_dbContext, panel.PageId, cancellationToken) is { } readOnly)
            return Result<PanelDto>.Failure(readOnly, ResultErrorCodes.ChapterReadOnly);
        if (command.ExpectedVersion is { } expected && expected != panel.Version)
            return Result<PanelDto>.Failure(
                $"Panel đã được người khác sửa (version hiện tại {panel.Version}, bạn đang sửa bản {expected}). Tải lại rồi thử lại.",
                ResultErrorCodes.Conflict);

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

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Hai người lưu gần như cùng lúc: Version là concurrency token nên bản lưu sau bị từ chối (BR-11)
            return Result<PanelDto>.Failure("Panel vừa được người khác sửa. Tải lại rồi thử lại.", ResultErrorCodes.Conflict);
        }

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
            SceneId: panel.SceneId,
            Version: panel.Version
        ));
    }
}
