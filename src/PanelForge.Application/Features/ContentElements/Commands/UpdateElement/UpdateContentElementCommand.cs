using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.ContentElements.Models;
using PanelForge.Application.Interfaces.Persistence;

namespace PanelForge.Application.Features.ContentElements.Commands.UpdateElement;

public sealed record UpdateContentElementCommand(
    Guid ElementId,
    Guid UserId,
    string? Content,
    string? TransformGeometry,
    string? StyleProperties,
    int? ZIndex,
    Guid? ScriptLineId,
    Guid? SpeakerCharacterId
) : IRequest<Result<ContentElementDto>>;

public sealed class UpdateContentElementCommandHandler : IRequestHandler<UpdateContentElementCommand, Result<ContentElementDto>>
{
    private readonly IPanelForgeDbContext _dbContext;

    public UpdateContentElementCommandHandler(IPanelForgeDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<ContentElementDto>> Handle(UpdateContentElementCommand command, CancellationToken cancellationToken)
    {
        var element = await _dbContext.Elements
            .FirstOrDefaultAsync(e => e.Id == command.ElementId, cancellationToken);

        if (element == null)
            return Result<ContentElementDto>.Failure($"Element {command.ElementId} không tồn tại.");

        element.UpdateContent(command.Content);

        if (!string.IsNullOrWhiteSpace(command.TransformGeometry))
        {
            element.UpdateTransform(command.TransformGeometry);
        }

        if (!string.IsNullOrWhiteSpace(command.StyleProperties))
        {
            element.UpdateStyle(command.StyleProperties);
        }

        if (command.ZIndex.HasValue)
        {
            element.UpdateZIndex(command.ZIndex.Value);
        }

        element.BindToScriptLine(command.ScriptLineId);
        element.AssignSpeaker(command.SpeakerCharacterId);
        element.UpdatedBy = command.UserId.ToString();

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result<ContentElementDto>.Success(new ContentElementDto(
            Id: element.Id,
            PanelId: element.PanelId,
            ElementType: element.ElementType,
            ZIndex: element.ZIndex,
            TransformGeometry: element.TransformGeometry,
            Content: element.Content,
            StyleProperties: element.StyleProperties,
            ScriptLineId: element.ScriptLineId,
            SpeakerCharacterId: element.SpeakerCharacterId,
            CreatedAt: element.CreatedAt,
            UpdatedAt: element.UpdatedAt,
            CreatedBy: element.CreatedBy
        ));
    }
}
