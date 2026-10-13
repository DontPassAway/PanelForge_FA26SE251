using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Chapters.Common;
using PanelForge.Application.Common.Interfaces.Persistence;

namespace PanelForge.Application.Features.Panels.Commands.DeletePanel;

public sealed record DeletePanelCommand(Guid PanelId, Guid UserId) : IRequest<Result<bool>>;

public sealed class DeletePanelCommandHandler : IRequestHandler<DeletePanelCommand, Result<bool>>
{
    private readonly IPanelForgeDbContext _dbContext;

    public DeletePanelCommandHandler(IPanelForgeDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<bool>> Handle(DeletePanelCommand command, CancellationToken cancellationToken)
    {
        var panel = await _dbContext.Panels
            .FirstOrDefaultAsync(p => p.Id == command.PanelId, cancellationToken);

        if (panel == null)
            return Result<bool>.Failure($"Panel {command.PanelId} không tồn tại.");
        if (await ChapterEditGuard.ReadOnlyReasonForPageAsync(_dbContext, panel.PageId, cancellationToken) is { } readOnly)
            return Result<bool>.Failure(readOnly, ResultErrorCodes.ChapterReadOnly);

        panel.DeletedBy = command.UserId.ToString();

        // Xóa thông thường -> DbContext tự động đổi thành Soft-delete
        _dbContext.Panels.Remove(panel);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result<bool>.Success(true);
    }
}
