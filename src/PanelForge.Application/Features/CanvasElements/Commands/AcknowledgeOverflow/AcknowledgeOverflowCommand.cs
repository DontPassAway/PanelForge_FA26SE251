using MediatR;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Chapters.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.CanvasElements.Common;
using PanelForge.Application.Features.Assignments.Common;

namespace PanelForge.Application.Features.CanvasElements.Commands.AcknowledgeOverflow;

/// <summary>BR-20: xác nhận cảnh báo tràn chữ kèm lý do để Lettering qua cổng thay vì bị chặn.</summary>
public sealed record AcknowledgeOverflowCommand(Guid PageId, Guid ElementId, string Reason, Guid UserId)
    : IRequest<Result<int>>;

public sealed class AcknowledgeOverflowCommandHandler : IRequestHandler<AcknowledgeOverflowCommand, Result<int>>
{
    private readonly IPageRepository _pages;
    private readonly IPanelForgeDbContext _db;

    public AcknowledgeOverflowCommandHandler(IPageRepository pages, IPanelForgeDbContext db)
    {
        _pages = pages;
        _db = db;
    }

    public async Task<Result<int>> Handle(AcknowledgeOverflowCommand cmd, CancellationToken ct)
    {
        var page = await _pages.GetAsync(cmd.PageId, ct);
        if (page is null) return Result<int>.Failure($"Page {cmd.PageId} không tồn tại.");
        if (await ChapterEditGuard.ReadOnlyReasonAsync(_db, page.ChapterId, ct) is { } readOnly)
            return Result<int>.Failure(readOnly, ResultErrorCodes.ChapterReadOnly);
        // Chỉ người giữ việc đang làm của phần này mới ghi (không đè phần đã chuyển giao cho người khác)
        if (await WorkScopeGuard.DenyReasonAsync(_db, cmd.UserId, page.Id, page.Elements.TryGetValue(cmd.ElementId, out var scoped) ? scoped.PanelId : null, ct) is { } outOfScope)
            return Result<int>.Failure(outOfScope, ResultErrorCodes.Forbidden);
        if (!page.Elements.TryGetValue(cmd.ElementId, out var element) || element.IsRemoved)
            return Result<int>.Failure($"Element {cmd.ElementId} không tồn tại trên Page {cmd.PageId}.");

        // Chỉ xác nhận được khi cảnh báo đang thật sự tồn tại (kiểm bằng code, không tin client)
        var analysis = await LetteringPageAnalysis.AnalyzeAsync(_db, page, ct);
        if (!analysis.Warnings.Any(w => w.ElementId == cmd.ElementId && w.Code == LetteringWarningRules.TextOverflow))
            return Result<int>.Failure("Element này hiện không có cảnh báo tràn chữ để xác nhận.");

        try
        {
            page.AcknowledgeOverflow(cmd.ElementId, cmd.Reason, cmd.UserId);
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
