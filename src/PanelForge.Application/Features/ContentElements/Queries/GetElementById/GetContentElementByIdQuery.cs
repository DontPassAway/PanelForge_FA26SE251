using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.ContentElements.Models;
using PanelForge.Application.Interfaces.Persistence;

namespace PanelForge.Application.Features.ContentElements.Queries.GetElementById;

public sealed record GetContentElementByIdQuery(Guid ElementId) : IRequest<Result<ContentElementDto>>;

public sealed class GetContentElementByIdQueryHandler : IRequestHandler<GetContentElementByIdQuery, Result<ContentElementDto>>
{
    private readonly IPanelForgeDbContext _dbContext;

    public GetContentElementByIdQueryHandler(IPanelForgeDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<ContentElementDto>> Handle(GetContentElementByIdQuery query, CancellationToken cancellationToken)
    {
        var element = await _dbContext.Elements
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == query.ElementId, cancellationToken);

        if (element == null)
            return Result<ContentElementDto>.Failure($"Element {query.ElementId} không tồn tại.");

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
