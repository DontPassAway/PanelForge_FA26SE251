using MediatR;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Chapters.Common;
using PanelForge.Application.Common.Interfaces.Persistence;

namespace PanelForge.Application.Features.CanvasElements.Commands.SetReadingOrder;

/// <summary>Letterer ghi đè thứ tự đọc của một text element (≥ 1), hoặc bỏ ghi đè bằng null.</summary>
public sealed record SetReadingOrderCommand(Guid PageId, Guid ElementId, int? ReadingOrder, Guid UserId, int? ExpectedElementVersion = null)
    : IRequest<Result<bool>>;

public sealed class SetReadingOrderCommandHandler : IRequestHandler<SetReadingOrderCommand, Result<bool>>
{
    private readonly IPageRepository _pages;
    private readonly IPanelForgeDbContext _db;

    public SetReadingOrderCommandHandler(IPageRepository pages, IPanelForgeDbContext db)
    {
        _pages = pages;
        _db = db;
    }

    public async Task<Result<bool>> Handle(SetReadingOrderCommand cmd, CancellationToken ct)
    {
        var page = await _pages.GetAsync(cmd.PageId, ct);
        if (page is null) return Result<bool>.Failure($"Page {cmd.PageId} không tồn tại.");
        if (await ChapterEditGuard.ReadOnlyReasonAsync(_db, page.ChapterId, ct) is { } readOnly)
            return Result<bool>.Failure(readOnly, ResultErrorCodes.ChapterReadOnly);

        try
        {
            page.EnsureElementVersion(cmd.ElementId, cmd.ExpectedElementVersion);
            page.SetReadingOrder(cmd.ElementId, cmd.ReadingOrder, cmd.UserId);
            await _pages.SaveAsync(page, ct);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or ConcurrentPageUpdateException)
        {
            return Result<bool>.Failure(ex.Message);
        }

        return Result<bool>.Success(true);
    }
}
