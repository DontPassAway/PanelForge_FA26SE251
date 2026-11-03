using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Chapters.Common;
using PanelForge.Application.Features.Pages.Common;
using PanelForge.Application.Features.Pages.Models;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Common.Interfaces.Persistence;

namespace PanelForge.Application.Features.Pages.Commands.ReorderPage;

public sealed record ReorderPageCommand(
    Guid PageId,
    int NewPageNumber,
    Guid UserId
) : IRequest<Result<PageDto>>;

public sealed class ReorderPageCommandHandler : IRequestHandler<ReorderPageCommand, Result<PageDto>>
{
    private readonly IPanelForgeDbContext _dbContext;
    private readonly IPageRepository _pageRepository;
    private readonly IUnitOfWork _unitOfWork;

    public ReorderPageCommandHandler(
        IPanelForgeDbContext dbContext,
        IPageRepository pageRepository,
        IUnitOfWork unitOfWork)
    {
        _dbContext = dbContext;
        _pageRepository = pageRepository;
        _unitOfWork = unitOfWork;
    }

    // Marten (event) + EF Core (read model) ghi trong cùng một transaction
    public Task<Result<PageDto>> Handle(ReorderPageCommand command, CancellationToken cancellationToken)
        => _unitOfWork.ExecuteInTransactionAsync(ct => ReorderAsync(command, ct), cancellationToken);

    private async Task<Result<PageDto>> ReorderAsync(ReorderPageCommand command, CancellationToken cancellationToken)
    {
        if (command.NewPageNumber <= 0)
            return Result<PageDto>.Failure("Số trang mới phải lớn hơn 0.");

        // 1. Load Page từ Repository (Marten Event Store)
        var page = await _pageRepository.GetAsync(command.PageId, cancellationToken);
        if (page == null)
            return Result<PageDto>.Failure($"Page {command.PageId} không tồn tại.");
        if (await ChapterEditGuard.ReadOnlyReasonAsync(_dbContext, page.ChapterId, cancellationToken) is { } readOnly)
            return Result<PageDto>.Failure(readOnly, ResultErrorCodes.ChapterReadOnly);

        // 2. Rút trang ra rồi chèn vào vị trí mới; các trang khác dời theo và được đánh số liền 1..N
        var order = await _dbContext.Pages.AsNoTracking()
            .Where(p => p.ChapterId == page.ChapterId && p.Id != page.Id)
            .OrderBy(p => p.PageNumber)
            .Select(p => p.Id)
            .ToListAsync(cancellationToken);
        order.Insert(Math.Min(command.NewPageNumber, order.Count + 1) - 1, page.Id);
        var newNumbers = order.Select((id, i) => (id, n: i + 1)).ToDictionary(x => x.id, x => x.n);

        // 3. Không cho đảo/cắt thứ tự Scene
        if (await PageOrdering.SceneOrderViolationAsync(_dbContext, page.ChapterId, newNumbers, cancellationToken) is { } violation)
            return Result<PageDto>.Failure(violation, ResultErrorCodes.Conflict);

        // 4. Append event vào Marten + cập nhật read model cho mọi trang đổi số
        if (await PageOrdering.ApplyAsync(_dbContext, _pageRepository, page.ChapterId, newNumbers,
                command.UserId.ToString(), cancellationToken) is { } error)
            return Result<PageDto>.Failure(error);
        page = await _pageRepository.GetAsync(command.PageId, cancellationToken) ?? page;

        return Result<PageDto>.Success(new PageDto(
            Id: page.Id,
            ChapterId: page.ChapterId,
            PageNumber: page.PageNumber,
            LayoutFormat: page.LayoutFormat.ToString(),
            WidthPx: page.WidthPx,
            HeightPx: page.HeightPx,
            Dpi: page.Dpi,
            ElementsCount: page.Elements.Count(e => !e.Value.IsRemoved)
        ));
    }
}
