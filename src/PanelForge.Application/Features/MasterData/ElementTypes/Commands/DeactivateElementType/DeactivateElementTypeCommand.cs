using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;

namespace PanelForge.Application.Features.MasterData.ElementTypes.Commands.DeactivateElementType;

public sealed record DeactivateElementTypeCommand(Guid Id) : IRequest<Result<string>>;

public sealed class DeactivateElementTypeCommandHandler
    : IRequestHandler<DeactivateElementTypeCommand, Result<string>>
{
    private readonly IPanelForgeDbContext _dbContext;
    public DeactivateElementTypeCommandHandler(IPanelForgeDbContext dbContext) => _dbContext = dbContext;

    public async Task<Result<string>> Handle(DeactivateElementTypeCommand cmd, CancellationToken ct)
    {
        var entity = await _dbContext.ElementTypes.FirstOrDefaultAsync(e => e.Id == cmd.Id, ct);
        if (entity is null) return Result<string>.Failure("ElementType không tồn tại.");
        entity.Deactivate();
        await _dbContext.SaveChangesAsync(ct);
        return Result<string>.Success($"ElementType '{entity.Code}' đã được vô hiệu hóa.");
    }
}
