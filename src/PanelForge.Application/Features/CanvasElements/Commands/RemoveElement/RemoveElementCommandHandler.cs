using MediatR;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Chapters.Common;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.Assignments.Common;

namespace PanelForge.Application.Features.CanvasElements.Commands.RemoveElement;

public sealed class RemoveElementCommandHandler
    : IRequestHandler<RemoveElementCommand, Result<bool>>
{
    private readonly IPageRepository _pageRepository;
    private readonly IPanelForgeDbContext _db;

    public RemoveElementCommandHandler(IPageRepository pageRepository, IPanelForgeDbContext db)
    {
        _pageRepository = pageRepository;
        _db = db;
    }

    public async Task<Result<bool>> Handle(
        RemoveElementCommand command,
        CancellationToken cancellationToken)
    {
        // 1. Load Page aggregate
        var page = await _pageRepository.GetAsync(command.PageId, cancellationToken);
        if (page is null)
            return Result<bool>.Failure($"Page {command.PageId} không tồn tại.");
        if (await ChapterEditGuard.ReadOnlyReasonAsync(_db, page.ChapterId, cancellationToken) is { } readOnly)
            return Result<bool>.Failure(readOnly, ResultErrorCodes.ChapterReadOnly);
        // Chỉ người giữ việc đang làm của phần này mới ghi (không đè phần đã chuyển giao cho người khác)
        if (await WorkScopeGuard.DenyReasonAsync(_db, command.RemovedByUserId, page.Id, page.Elements.TryGetValue(command.ElementId, out var scoped) ? scoped.PanelId : null, cancellationToken) is { } outOfScope)
            return Result<bool>.Failure(outOfScope, ResultErrorCodes.Forbidden);

        // 2. Domain method — validate element tồn tại và chưa bị xóa
        try
        {
            page.EnsureElementVersion(command.ElementId, command.ExpectedElementVersion);
            page.RemoveElement(
                elementId:       command.ElementId,
                reason:          command.Reason,
                removedByUserId: command.RemovedByUserId
            );
        }
        catch (InvalidOperationException ex)
        {
            return Result<bool>.Failure(ex.Message);
        }

        // 3. Persist ElementRemovedEvent — thao tác APPEND duy nhất, KHÔNG có DELETE SQL
        try
        {
            await _pageRepository.SaveAsync(page, cancellationToken);
        }
        catch (ConcurrentPageUpdateException ex)
        {
            return Result<bool>.Failure(ex.Message);
        }

        return Result<bool>.Success(true);
    }
}
