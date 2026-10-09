using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.MasterData.ElementTypes.Models;
using PanelForge.Application.Features.MasterData.ElementTypes.Common;

namespace PanelForge.Application.Features.MasterData.ElementTypes.Commands.UpdateElementType;

public sealed record UpdateElementTypeCommand(
    Guid Id, string Name, string? Description, string? AllowedPropertiesJson, bool IsActive
) : IRequest<Result<ElementTypeDto>>;

public sealed class UpdateElementTypeCommandHandler
    : IRequestHandler<UpdateElementTypeCommand, Result<ElementTypeDto>>
{
    private readonly IPanelForgeDbContext _dbContext;
    public UpdateElementTypeCommandHandler(IPanelForgeDbContext dbContext) => _dbContext = dbContext;

    public async Task<Result<ElementTypeDto>> Handle(UpdateElementTypeCommand cmd, CancellationToken ct)
    {
        var entity = await _dbContext.ElementTypes.FirstOrDefaultAsync(e => e.Id == cmd.Id, ct);
        if (entity is null) return Result<ElementTypeDto>.Failure("ElementType không tồn tại.");

        try { entity.Update(cmd.Name, cmd.Description, cmd.AllowedPropertiesJson); }
        catch (ArgumentException ex) { return Result<ElementTypeDto>.Failure(ex.Message); }

        if (!cmd.IsActive) entity.Deactivate(); else entity.Activate();

        await _dbContext.SaveChangesAsync(ct);
        return Result<ElementTypeDto>.Success(entity.ToDto());
    }
}
