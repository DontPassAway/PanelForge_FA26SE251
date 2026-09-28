using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Interfaces.Persistence;

namespace PanelForge.Application.Features.Pages.DeletePage;

public sealed class DeletePageCommandHandler
    : IRequestHandler<DeletePageCommand, Result<bool>>
{
    private readonly IPanelForgeDbContext _dbContext;

    public DeletePageCommandHandler(IPanelForgeDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<bool>> Handle(
        DeletePageCommand command,
        CancellationToken cancellationToken)
    {
        var page = await _dbContext.Pages
            .FirstOrDefaultAsync(p => p.Id == command.PageId, cancellationToken);

        if (page is null)
            return Result<bool>.Failure($"Page {command.PageId} không tồn tại.");

        _dbContext.Pages.Remove(page);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result<bool>.Success(true);
    }
}
