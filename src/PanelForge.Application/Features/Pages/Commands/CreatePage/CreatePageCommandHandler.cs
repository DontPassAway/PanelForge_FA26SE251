using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Chapters.Common;
using PanelForge.Application.Features.Pages.Common;
using PanelForge.Application.Features.Pages.Models;
using PanelForge.Application.Features.ScriptLines.Common;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Pages.Commands.CreatePage;

public sealed class CreatePageCommandHandler
    : IRequestHandler<CreatePageCommand, Result<PageDto>>
{
    private readonly IPanelForgeDbContext _dbContext;
    private readonly IPageRepository _pageRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IWorkspaceAuthorizationService _auth;

    public CreatePageCommandHandler(
        IPanelForgeDbContext dbContext,
        IPageRepository pageRepository,
        IUnitOfWork unitOfWork,
        IWorkspaceAuthorizationService auth)
    {
        _dbContext = dbContext;
        _pageRepository = pageRepository;
        _unitOfWork = unitOfWork;
        _auth = auth;
    }

    // Marten (event) + EF Core (read model) ghi trong cùng một transaction
    public Task<Result<PageDto>> Handle(
        CreatePageCommand command,
        CancellationToken cancellationToken)
        => _unitOfWork.ExecuteInTransactionAsync(ct => CreateAsync(command, ct), cancellationToken);

    private async Task<Result<PageDto>> CreateAsync(
        CreatePageCommand command,
        CancellationToken cancellationToken)
    {
        // 1. Kiểm tra Chapter có tồn tại không
        var chapterExists = await _dbContext.Chapters
            .AnyAsync(c => c.Id == command.ChapterId, cancellationToken);

        if (!chapterExists)
            return Result<PageDto>.Failure($"Chapter {command.ChapterId} không tồn tại.");
        if (await ChapterEditGuard.ReadOnlyReasonAsync(_dbContext, command.ChapterId, cancellationToken) is { } readOnly)
            return Result<PageDto>.Failure(readOnly, ResultErrorCodes.ChapterReadOnly);
        if (command.UserId is { } userId &&
            await ScriptLineRules.WriterStructureBlockedReasonAsync(_dbContext, _auth, userId, command.ChapterId, cancellationToken) is { } writerBlock)
            return Result<PageDto>.Failure(writerBlock.Error, writerBlock.Code);

        // 2. Tính số trang tự động nếu không truyền vào
        int pageNumber;
        if (command.PageNumber.HasValue && command.PageNumber.Value > 0)
        {
            pageNumber = command.PageNumber.Value;
            var isDuplicate = await _dbContext.Pages
                .AnyAsync(p => p.ChapterId == command.ChapterId && p.PageNumber == pageNumber, cancellationToken);

            // Chèn giữa: dời các trang từ vị trí này trở đi lùi một số (trang mới trống nên không đảo thứ tự Scene)
            if (isDuplicate)
            {
                var shifted = await _dbContext.Pages.AsNoTracking()
                    .Where(p => p.ChapterId == command.ChapterId && p.PageNumber >= pageNumber)
                    .ToDictionaryAsync(p => p.Id, p => p.PageNumber + 1, cancellationToken);
                if (await PageOrdering.ApplyAsync(_dbContext, _pageRepository, command.ChapterId, shifted,
                        null, cancellationToken) is { } error)
                    return Result<PageDto>.Failure(error);
            }
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
            page = Page.CreateNew(
                chapterId:    command.ChapterId,
                pageNumber:   pageNumber,
                layoutFormat: layoutFormat,
                widthPx:      command.WidthPx,
                heightPx:     command.HeightPx,
                dpi:          command.Dpi
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
            PageNumber:    page.PageNumber,
            LayoutFormat:  page.LayoutFormat.ToString(),
            WidthPx:       page.WidthPx,
            HeightPx:      page.HeightPx,
            Dpi:           page.Dpi,
            ElementsCount: 0
        ));
    }
}
