using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Chapters.Common;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Common.Interfaces.Persistence;

namespace PanelForge.Application.Features.Pages.Commands.DeletePage;

/// <summary>
/// Xóa mềm Page ở CẢ HAI nơi trong cùng một transaction:
///   - Marten: append PageDeletedEvent (stream được giữ lại, append-only)
///   - EF Core: soft-delete bản ghi bảng pages (DbContext chuyển Remove → IsDeleted = true)
/// Sau đó IPageRepository.GetAsync trả null → các thao tác Element trên Page này bị từ chối.
/// </summary>
public sealed class DeletePageCommandHandler
    : IRequestHandler<DeletePageCommand, Result<bool>>
{
    private readonly IPanelForgeDbContext _dbContext;
    private readonly IPageRepository _pageRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _currentUser;

    public DeletePageCommandHandler(
        IPanelForgeDbContext dbContext,
        IPageRepository pageRepository,
        IUnitOfWork unitOfWork,
        ICurrentUserService currentUser)
    {
        _dbContext = dbContext;
        _pageRepository = pageRepository;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
    }

    public Task<Result<bool>> Handle(
        DeletePageCommand command,
        CancellationToken cancellationToken)
        => _unitOfWork.ExecuteInTransactionAsync(ct => DeleteAsync(command, ct), cancellationToken);

    private async Task<Result<bool>> DeleteAsync(
        DeletePageCommand command,
        CancellationToken cancellationToken)
    {
        var efPage = await _dbContext.Pages
            .FirstOrDefaultAsync(p => p.Id == command.PageId, cancellationToken);
        var streamPage = await _pageRepository.GetAsync(command.PageId, cancellationToken);

        if (efPage is null && streamPage is null)
            return Result<bool>.Failure($"Page {command.PageId} không tồn tại.");
        if (await ChapterEditGuard.ReadOnlyReasonAsync(_dbContext, efPage?.ChapterId ?? streamPage!.ChapterId, cancellationToken) is { } readOnly)
            return Result<bool>.Failure(readOnly, ResultErrorCodes.ChapterReadOnly);

        // 1. Event Store: đánh dấu Page đã xóa trong stream
        if (streamPage is not null)
        {
            streamPage.Delete(_currentUser.UserId);
            await _pageRepository.SaveAsync(streamPage, cancellationToken);
        }

        // 2. Read model: soft-delete bảng pages
        if (efPage is not null)
        {
            _dbContext.Pages.Remove(efPage);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return Result<bool>.Success(true);
    }
}
