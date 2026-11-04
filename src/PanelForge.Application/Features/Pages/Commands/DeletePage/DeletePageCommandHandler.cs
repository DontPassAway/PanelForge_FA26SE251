using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Chapters.Common;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Assignments.Common;
using PanelForge.Application.Features.Pages.Common;

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

        // Còn việc đang giao cho Page hoặc Panel của nó thì không xoá: tránh Assignment mồ côi
        var targetIds = await _dbContext.Panels.AsNoTracking()
            .Where(p => p.PageId == command.PageId).Select(p => p.Id).ToListAsync(cancellationToken);
        targetIds.Add(command.PageId);
        if (await AssignmentRules.DeleteBlockedReasonAsync(_dbContext, "trang", targetIds, cancellationToken) is { } blocked)
            return Result<bool>.Failure(blocked, ResultErrorCodes.Conflict);

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

        // 3. Đánh số lại các trang còn lại thành 1..N (xoá trang 1 thì trang 2, 3 thành 1, 2)
        var chapterId = efPage?.ChapterId ?? streamPage!.ChapterId;
        var remaining = await _dbContext.Pages.AsNoTracking()
            .Where(p => p.ChapterId == chapterId && p.Id != command.PageId)
            .OrderBy(p => p.PageNumber)
            .Select(p => p.Id)
            .ToListAsync(cancellationToken);
        if (await PageOrdering.ApplyAsync(_dbContext, _pageRepository, chapterId,
                remaining.Select((id, i) => (id, n: i + 1)).ToDictionary(x => x.id, x => x.n),
                _currentUser.UserId?.ToString(), cancellationToken) is { } renumberError)
            return Result<bool>.Failure(renumberError);

        return Result<bool>.Success(true);
    }
}
