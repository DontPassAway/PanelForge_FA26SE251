using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Interfaces.Persistence;
using PanelForge.Domain.Entities.MasterData;

namespace PanelForge.Application.Features.MasterData.ElementTypes.Commands;

// ─── DTOs ─────────────────────────────────────────────────────────────────────

public sealed record ElementTypeDto(
    Guid Id, string Code, string Name, string? Description,
    string AllowedPropertiesJson, bool IsActive, DateTime CreatedAt, DateTime? UpdatedAt);

public static class ElementTypeMasterDataExtensions
{
    public static ElementTypeDto ToDto(this ElementTypeMasterData e) => new(
        e.Id, e.Code, e.Name, e.Description, e.AllowedPropertiesJson, e.IsActive, e.CreatedAt, e.UpdatedAt);
}

// ─── HTTP Request Models ───────────────────────────────────────────────────────

public sealed record CreateElementTypeRequest(string Code, string Name, string? Description, string? AllowedPropertiesJson);
public sealed record UpdateElementTypeRequest(string Name, string? Description, string? AllowedPropertiesJson, bool IsActive);

// ─── Create ───────────────────────────────────────────────────────────────────

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

// ─── Update ───────────────────────────────────────────────────────────────────

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

// ─── Deactivate (soft-delete) ─────────────────────────────────────────────────

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
