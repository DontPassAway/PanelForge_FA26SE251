using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Domain.Entities.MasterData;
using PanelForge.Application.Features.MasterData.ElementTypes.Models;
using PanelForge.Application.Features.MasterData.ElementTypes.Common;

namespace PanelForge.Application.Features.MasterData.ElementTypes.Commands.CreateElementType;

public sealed record CreateElementTypeCommand(
    string Code, string Name, string? Description, string? AllowedPropertiesJson
) : IRequest<Result<ElementTypeDto>>;

public sealed class CreateElementTypeCommandHandler
    : IRequestHandler<CreateElementTypeCommand, Result<ElementTypeDto>>
{
    private readonly IPanelForgeDbContext _dbContext;
    public CreateElementTypeCommandHandler(IPanelForgeDbContext dbContext) => _dbContext = dbContext;

    public async Task<Result<ElementTypeDto>> Handle(CreateElementTypeCommand cmd, CancellationToken ct)
    {
        var code = cmd.Code.Trim().ToUpperInvariant();
        var exists = await _dbContext.ElementTypes.AnyAsync(e => e.Code == code, ct);
        if (exists) return Result<ElementTypeDto>.Failure($"ElementType với Code '{code}' đã tồn tại.");

        ElementTypeMasterData entity;
        try { entity = ElementTypeMasterData.Create(cmd.Code, cmd.Name, cmd.Description, cmd.AllowedPropertiesJson); }
        catch (ArgumentException ex) { return Result<ElementTypeDto>.Failure(ex.Message); }

        _dbContext.ElementTypes.Add(entity);
        await _dbContext.SaveChangesAsync(ct);
        return Result<ElementTypeDto>.Success(entity.ToDto());
    }
}
