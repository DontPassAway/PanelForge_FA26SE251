using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.ContentElements.Models;
using PanelForge.Application.Interfaces.Persistence;
using DomainElement = PanelForge.Domain.Entities.Content.Element;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.ContentElements.Commands.CreateElement;

public sealed record CreateContentElementCommand(
    Guid PanelId,
    Guid UserId,
    ElementType ElementType,
    int ZIndex,
    string? Content,
    string? TransformGeometry,
    string? StyleProperties,
    Guid? ScriptLineId,
    Guid? SpeakerCharacterId
) : IRequest<Result<ContentElementDto>>;

public sealed class CreateContentElementCommandHandler : IRequestHandler<CreateContentElementCommand, Result<ContentElementDto>>
{
    private readonly IPanelForgeDbContext _dbContext;

    public CreateContentElementCommandHandler(IPanelForgeDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<ContentElementDto>> Handle(CreateContentElementCommand command, CancellationToken cancellationToken)
    {
        // 1. Kiểm tra Panel có tồn tại không
        var panelExists = await _dbContext.Panels
            .AnyAsync(p => p.Id == command.PanelId, cancellationToken);

        if (!panelExists)
            return Result<ContentElementDto>.Failure($"Panel {command.PanelId} không tồn tại.");

        // 2. Khởi tạo Element
        var element = DomainElement.Create(
            panelId: command.PanelId,
            elementType: command.ElementType,
            zIndex: command.ZIndex,
            content: command.Content,
            transformGeometry: command.TransformGeometry ?? "{}",
            styleProperties: command.StyleProperties ?? "{}",
            scriptLineId: command.ScriptLineId,
            speakerCharacterId: command.SpeakerCharacterId
        );
        element.CreatedBy = command.UserId.ToString();

        _dbContext.Elements.Add(element);
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
