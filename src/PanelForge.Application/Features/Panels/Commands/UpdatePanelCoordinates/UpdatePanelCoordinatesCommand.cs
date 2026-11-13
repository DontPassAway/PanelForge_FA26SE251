using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Assignments.Common;
using PanelForge.Application.Features.Chapters.Common;
using PanelForge.Application.Features.Panels.Models;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Domain.Enums;

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
        // Chỉ người giữ việc đang làm của phần này mới ghi (không đè phần đã chuyển giao cho người khác)
        if (await WorkScopeGuard.DenyReasonAsync(_dbContext, command.UserId, panel.PageId, panel.Id, cancellationToken) is { } outOfScope)
            return Result<PanelDto>.Failure(outOfScope, ResultErrorCodes.Forbidden);
        if (command.ExpectedVersion is { } expected && expected != panel.Version)
            return Result<PanelDto>.Failure(
                $"Panel đã được người khác sửa (version hiện tại {panel.Version}, bạn đang sửa bản {expected}). Tải lại rồi thử lại.",
                ResultErrorCodes.Conflict);

        var boxBefore = panel.BoundingBox;
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

        // CF4 (BR-06): đổi khung Panel đã có artwork/chữ được duyệt → các việc đó của đúng Panel này phải làm lại (cùng lần Save)
        if (panel.BoundingBox != boxBefore)
        {
            var chapterId = await _dbContext.Pages.AsNoTracking()
                .Where(p => p.Id == panel.PageId).Select(p => p.ChapterId).FirstAsync(cancellationToken);
            var (_, reworkError) = await StageProgressRules.ReworkForSourceChangeAsync(_dbContext, chapterId,
                new HashSet<Guid> { panel.Id }, [WorkspaceRole.Artist, WorkspaceRole.Letterer],
                $"Khung Panel {panel.PanelNumber} đã đổi sau khi duyệt, cần làm lại artwork/chữ của Panel này.", cancellationToken);
            if (reworkError is not null) return Result<PanelDto>.Failure(reworkError, ResultErrorCodes.Conflict);
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
            Version: panel.Version,
            Direction: panel.Direction
        ));
    }
}
