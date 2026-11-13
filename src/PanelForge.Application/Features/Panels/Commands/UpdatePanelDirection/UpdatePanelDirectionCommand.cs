using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Assignments.Common;
using PanelForge.Application.Features.Chapters.Common;
using PanelForge.Application.Features.Panels.Models;
using PanelForge.Application.Common.Interfaces.Persistence;

namespace PanelForge.Application.Features.Panels.Commands.UpdatePanelDirection;

public sealed record UpdatePanelDirectionCommand(
    Guid PanelId,
    Guid UserId,
    string? Direction,
    int? ExpectedVersion = null
) : IRequest<Result<PanelDto>>;

public sealed class UpdatePanelDirectionCommandHandler : IRequestHandler<UpdatePanelDirectionCommand, Result<PanelDto>>
{
    private readonly IPanelForgeDbContext _dbContext;

    public UpdatePanelDirectionCommandHandler(IPanelForgeDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<PanelDto>> Handle(UpdatePanelDirectionCommand command, CancellationToken cancellationToken)
    {
        var panel = await _dbContext.Panels
            .FirstOrDefaultAsync(p => p.Id == command.PanelId, cancellationToken);

        if (panel == null)
            return Result<PanelDto>.Failure($"Panel {command.PanelId} khÃ´ng tá»“n táº¡i.");

        if (await ChapterEditGuard.ReadOnlyReasonForPageAsync(_dbContext, panel.PageId, cancellationToken) is { } readOnly)
            return Result<PanelDto>.Failure(readOnly, ResultErrorCodes.ChapterReadOnly);

        if (await WorkScopeGuard.DenyReasonAsync(_dbContext, command.UserId, panel.PageId, panel.Id, cancellationToken) is { } outOfScope)
            return Result<PanelDto>.Failure(outOfScope, ResultErrorCodes.Forbidden);

        if (command.ExpectedVersion is { } expected && expected != panel.Version)
            return Result<PanelDto>.Failure(
                $"Panel Ä‘Ã£ Ä‘Æ°á»£c ngÆ°á» i khÃ¡c sá»­a (version hiá»‡n táº¡i {panel.Version}, báº¡n Ä‘ang sá»­a báº£n {expected}). Táº£i láº¡i rá»“i thá»­ láº¡i.",
                ResultErrorCodes.Conflict);

        try
        {
            panel.UpdateDirection(command.Direction);
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
            return Result<PanelDto>.Failure("Panel vá»«a Ä‘Æ°á»£c ngÆ°á» i khÃ¡c sá»­a. Táº£i láº¡i rá»“i thá»­ láº¡i.", ResultErrorCodes.Conflict);
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
