using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Interfaces.Persistence;

namespace PanelForge.Application.Features.ContentElements.Commands.DeleteElement;

public sealed record DeleteContentElementCommand(Guid ElementId, Guid UserId) : IRequest<Result<bool>>;

public sealed class DeleteContentElementCommandHandler : IRequestHandler<DeleteContentElementCommand, Result<bool>>
{
    private readonly IPanelForgeDbContext _dbContext;

    public DeleteContentElementCommandHandler(IPanelForgeDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<bool>> Handle(DeleteContentElementCommand command, CancellationToken cancellationToken)
    {
        var element = await _dbContext.Elements
            .FirstOrDefaultAsync(e => e.Id == command.ElementId, cancellationToken);

        if (element == null)
            return Result<bool>.Failure($"Element {command.ElementId} không tồn tại.");

        element.DeletedBy = command.UserId.ToString();

        // Xóa thông thường -> DbContext tự động ép thành Soft-delete
        _dbContext.Elements.Remove(element);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result<bool>.Success(true);
    }
}
