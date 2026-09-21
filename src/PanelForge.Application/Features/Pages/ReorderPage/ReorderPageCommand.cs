using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.DTOs.Content;
using PanelForge.Application.Interfaces;
using PanelForge.Application.Interfaces.Persistence;

namespace PanelForge.Application.Features.Pages.ReorderPage;

public sealed record ReorderPageCommand(
    Guid PageId,
    int NewPageNumber,
    Guid UserId
) : IRequest<Result<PageDto>>;

public sealed class ReorderPageCommandHandler : IRequestHandler<ReorderPageCommand, Result<PageDto>>
{
    private readonly IPanelForgeDbContext _dbContext;
    private readonly IPageRepository _pageRepository;

    public ReorderPageCommandHandler(
        IPanelForgeDbContext dbContext,
        IPageRepository pageRepository)
    {
        _dbContext = dbContext;
        _pageRepository = pageRepository;
    }

    public async Task<Result<PageDto>> Handle(ReorderPageCommand command, CancellationToken cancellationToken)
    {
        if (command.NewPageNumber <= 0)
            return Result<PageDto>.Failure("Số trang mới phải lớn hơn 0.");

        // 1. Load Page từ Repository (Marten Event Store)
        var page = await _pageRepository.GetAsync(command.PageId, cancellationToken);
        if (page == null)
            return Result<PageDto>.Failure($"Page {command.PageId} không tồn tại.");

        // 2. Reorder domain command
        try
        {
            page.Reorder(command.NewPageNumber);
            page.UpdatedBy = command.UserId.ToString();
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return Result<PageDto>.Failure(ex.Message);
        }

        // 3. Persist Event vào Marten
        await _pageRepository.SaveAsync(page, cancellationToken);

        // 4. Update Read model trong EF Core
        var dbPage = await _dbContext.Pages.FirstOrDefaultAsync(p => p.Id == command.PageId, cancellationToken);
        if (dbPage != null)
        {
            dbPage.Reorder(command.NewPageNumber);
            dbPage.UpdatedBy = command.UserId.ToString();
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return Result<PageDto>.Success(new PageDto(
            Id: page.Id,
            ChapterId: page.ChapterId,
            SceneId: page.SceneId,
            PageNumber: page.PageNumber,
            LayoutFormat: page.LayoutFormat.ToString(),
            WidthPx: page.WidthPx,
            HeightPx: page.HeightPx,
            Dpi: page.Dpi,
            ElementsCount: page.Elements.Count(e => !e.Value.IsRemoved)
        ));
    }
}
