using MediatR;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Chapters.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Assignments.Common;

namespace PanelForge.Application.Features.CanvasElements.Commands.SetReadingOrder;

/// <summary>Letterer ghi đè thứ tự đọc của một text element (≥ 1), hoặc bỏ ghi đè bằng null.</summary>
public sealed record SetReadingOrderCommand(Guid PageId, Guid ElementId, int? ReadingOrder, Guid UserId, int? ExpectedElementVersion = null)
    : IRequest<Result<int>>;

public sealed class SetReadingOrderCommandHandler : IRequestHandler<SetReadingOrderCommand, Result<int>>
{
    private readonly IPageRepository _pages;
    private readonly IPanelForgeDbContext _db;

    public SetReadingOrderCommandHandler(IPageRepository pages, IPanelForgeDbContext db)
    {
        _pages = pages;
        _db = db;
    }

    public async Task<Result<int>> Handle(SetReadingOrderCommand cmd, CancellationToken ct)
    {
        var page = await _pages.GetAsync(cmd.PageId, ct);
        if (page is null) return Result<int>.Failure($"Page {cmd.PageId} không tồn tại.");
        if (await ChapterEditGuard.ReadOnlyReasonAsync(_db, page.ChapterId, ct) is { } readOnly)
            return Result<int>.Failure(readOnly, ResultErrorCodes.ChapterReadOnly);
        // Chỉ người giữ việc đang làm của phần này mới ghi (không đè phần đã chuyển giao cho người khác)
        if (await WorkScopeGuard.DenyReasonAsync(_db, cmd.UserId, page.Id, page.Elements.TryGetValue(cmd.ElementId, out var scoped) ? scoped.PanelId : null, ct) is { } outOfScope)
            return Result<int>.Failure(outOfScope, ResultErrorCodes.Forbidden);

        try
        {
            page.EnsureElementVersion(cmd.ElementId, cmd.ExpectedElementVersion);
            page.SetReadingOrder(cmd.ElementId, cmd.ReadingOrder, cmd.UserId);
            await _pages.SaveAsync(page, ct);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or ConcurrentPageUpdateException)
        {
            return Result<int>.Failure(ex.Message);
        }

        // Phiên bản thật sau khi lưu (thao tác không đổi gì thì không tăng) — client dùng số này, không tự +1
        return Result<int>.Success(page.Elements[cmd.ElementId].Version);
    }
}
