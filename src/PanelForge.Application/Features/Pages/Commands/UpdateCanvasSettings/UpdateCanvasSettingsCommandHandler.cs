using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Interfaces;
using PanelForge.Application.Interfaces.Persistence;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Pages.UpdateCanvasSettings;

public sealed class UpdateCanvasSettingsCommandHandler
    : IRequestHandler<UpdateCanvasSettingsCommand, Result<bool>>
{
    private readonly IPageRepository _pageRepository;
    private readonly IPanelForgeDbContext _dbContext;

    public UpdateCanvasSettingsCommandHandler(
        IPageRepository pageRepository,
        IPanelForgeDbContext dbContext)
    {
        _pageRepository = pageRepository;
        _dbContext = dbContext;
    }

    public async Task<Result<bool>> Handle(
        UpdateCanvasSettingsCommand command,
        CancellationToken cancellationToken)
    {
        // 1. Nạp Page từ Marten Event Store
        var page = await _pageRepository.GetAsync(command.PageId, cancellationToken);
        if (page is null)
            return Result<bool>.Failure($"Page {command.PageId} không tồn tại.");

        if (!Enum.TryParse<LayoutFormat>(command.LayoutFormat, ignoreCase: true, out var layoutFormat))
        {
            return Result<bool>.Failure($"LayoutFormat '{command.LayoutFormat}' không hợp lệ.");
        }

        // 2. Domain method: Validate và raise PageCanvasUpdatedEvent
        try
        {
            page.UpdateCanvasSettings(command.WidthPx, command.HeightPx, command.Dpi, layoutFormat);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return Result<bool>.Failure(ex.Message);
        }

        // 3. Write Model: Lưu event vào Marten Event Store
        await _pageRepository.SaveAsync(page, cancellationToken);

        // 4. Read Model: Đồng bộ bảng pages trong EF Core
        var efPage = await _dbContext.Pages
            .FirstOrDefaultAsync(p => p.Id == command.PageId, cancellationToken);

        if (efPage is not null)
        {
            efPage.UpdateCanvasSettings(command.WidthPx, command.HeightPx, command.Dpi, layoutFormat);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return Result<bool>.Success(true);
    }
}
