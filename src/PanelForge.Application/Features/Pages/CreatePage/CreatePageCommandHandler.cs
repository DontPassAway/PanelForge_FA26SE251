using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.DTOs.Content;
using PanelForge.Application.Interfaces;
using PanelForge.Application.Interfaces.Persistence;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Pages.CreatePage;

public sealed class CreatePageCommandHandler
    : IRequestHandler<CreatePageCommand, Result<PageDto>>
{
    private readonly IPanelForgeDbContext _dbContext;
    private readonly IPageRepository _pageRepository;

    public CreatePageCommandHandler(
        IPanelForgeDbContext dbContext,
        IPageRepository pageRepository)
    {
        _dbContext = dbContext;
        _pageRepository = pageRepository;
    }

    public async Task<Result<PageDto>> Handle(
        CreatePageCommand command,
        CancellationToken cancellationToken)
    {
        // 1. Kiểm tra Chapter có tồn tại không
        var chapterExists = await _dbContext.Chapters
            .AnyAsync(c => c.Id == command.ChapterId, cancellationToken);

        if (!chapterExists)
            return Result<PageDto>.Failure($"Chapter {command.ChapterId} không tồn tại.");

        // 2. Tính số trang tự động nếu không truyền vào
        int pageNumber;
        if (command.PageNumber.HasValue && command.PageNumber.Value > 0)
        {
            pageNumber = command.PageNumber.Value;
            var isDuplicate = await _dbContext.Pages
                .AnyAsync(p => p.ChapterId == command.ChapterId && p.PageNumber == pageNumber, cancellationToken);

            if (isDuplicate)
                return Result<PageDto>.Failure($"Trang số {pageNumber} đã tồn tại trong Chapter này.");
        }
        else
        {
            var maxPage = await _dbContext.Pages
                .Where(p => p.ChapterId == command.ChapterId)
                .MaxAsync(p => (int?)p.PageNumber, cancellationToken);
            pageNumber = (maxPage ?? 0) + 1;
        }

        // 3. Parse LayoutFormat
        if (!Enum.TryParse<LayoutFormat>(command.LayoutFormat, ignoreCase: true, out var layoutFormat))
        {
            layoutFormat = LayoutFormat.StandardPage;
        }

        // 4. Khởi tạo Page Aggregate Root (Domain Event: PageCreatedEvent)
        Page page;
        try
        {
            page = Page.Create(
                chapterId:    command.ChapterId,
                pageNumber:   pageNumber,
                layoutFormat: layoutFormat,
                widthPx:      command.WidthPx,
                heightPx:     command.HeightPx,
                dpi:          command.Dpi,
                sceneId:      command.SceneId
            );
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return Result<PageDto>.Failure(ex.Message);
        }

        // 5. Write Model: Persist PageCreatedEvent vào Marten Event Store
        await _pageRepository.SaveAsync(page, cancellationToken);

        // 6. Read Model: Lưu vào EF Core bảng pages để phục vụ relational queries nhanh
        _dbContext.Pages.Add(page);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result<PageDto>.Success(new PageDto(
            Id:            page.Id,
            ChapterId:     page.ChapterId,
            SceneId:       page.SceneId,
            PageNumber:    page.PageNumber,
            LayoutFormat:  page.LayoutFormat.ToString(),
            WidthPx:       page.WidthPx,
            HeightPx:      page.HeightPx,
            Dpi:           page.Dpi,
            ElementsCount: 0
        ));
    }
}
