using MediatR;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.CanvasElements.Common;

namespace PanelForge.Application.Features.CanvasElements.Commands.AcknowledgeOverflow;

/// <summary>BR-20: xác nhận cảnh báo tràn chữ kèm lý do để Lettering qua cổng thay vì bị chặn.</summary>
public sealed record AcknowledgeOverflowCommand(Guid PageId, Guid ElementId, string Reason, Guid UserId)
    : IRequest<Result<bool>>;

public sealed class AcknowledgeOverflowCommandHandler : IRequestHandler<AcknowledgeOverflowCommand, Result<bool>>
{
    private readonly IPageRepository _pages;
    private readonly IPanelForgeDbContext _db;

    public AcknowledgeOverflowCommandHandler(IPageRepository pages, IPanelForgeDbContext db)
    {
        _pages = pages;
        _db = db;
    }

    public async Task<Result<bool>> Handle(AcknowledgeOverflowCommand cmd, CancellationToken ct)
    {
        var page = await _pages.GetAsync(cmd.PageId, ct);
        if (page is null) return Result<bool>.Failure($"Page {cmd.PageId} không tồn tại.");
        if (!page.Elements.TryGetValue(cmd.ElementId, out var element) || element.IsRemoved)
            return Result<bool>.Failure($"Element {cmd.ElementId} không tồn tại trên Page {cmd.PageId}.");

        // Chỉ xác nhận được khi cảnh báo đang thật sự tồn tại (kiểm bằng code, không tin client)
        var analysis = await LetteringPageAnalysis.AnalyzeAsync(_db, page, ct);
        if (!analysis.Warnings.Any(w => w.ElementId == cmd.ElementId && w.Code == LetteringWarningRules.TextOverflow))
            return Result<bool>.Failure("Element này hiện không có cảnh báo tràn chữ để xác nhận.");

        try
        {
            page.AcknowledgeOverflow(cmd.ElementId, cmd.Reason, cmd.UserId);
            await _pages.SaveAsync(page, ct);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or ConcurrentPageUpdateException)
        {
            return Result<bool>.Failure(ex.Message);
        }

        return Result<bool>.Success(true);
    }
}
