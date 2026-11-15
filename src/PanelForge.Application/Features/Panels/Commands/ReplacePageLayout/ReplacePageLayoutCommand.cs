using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Assignments.Common;
using PanelForge.Application.Features.Panels.Models;
using PanelForge.Application.Features.Panels.Services;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Panels.Commands.ReplacePageLayout;

public sealed record ReplacePageLayoutCommand(
    Guid PageId,
    Guid UserId,
    IReadOnlyList<CreatePanelRequest> Panels) : IRequest<Result<PageLayoutDto>>;

public sealed class ReplacePageLayoutCommandHandler : IRequestHandler<ReplacePageLayoutCommand, Result<PageLayoutDto>>
{
    private readonly IPanelForgeDbContext _db;
    private readonly IPageRepository _pages;
    private readonly PageLayoutReplacer _replacer;

    public ReplacePageLayoutCommandHandler(IPanelForgeDbContext db, IPageRepository pages, PageLayoutReplacer replacer)
    {
        _db = db;
        _pages = pages;
        _replacer = replacer;
    }

    public async Task<Result<PageLayoutDto>> Handle(ReplacePageLayoutCommand command, CancellationToken ct)
    {
        var status = await _db.Pages.Where(p => p.Id == command.PageId)
            .Select(p => (ChapterStatus?)p.Chapter.Status)
            .FirstOrDefaultAsync(ct);
        if (status is null)
            return Result<PageLayoutDto>.Failure($"Page {command.PageId} không tồn tại.", ResultErrorCodes.NotFound);
        if (status != ChapterStatus.InProduction)
            return Result<PageLayoutDto>.Failure("Chương không còn ở trạng thái sản xuất, không dựng lại bố cục được (BR-12).", ResultErrorCodes.Conflict);
        if (await WorkScopeGuard.DenyReasonAsync(_db, command.UserId, command.PageId, null, ct, layout: true) is { } outOfScope)
            return Result<PageLayoutDto>.Failure(outOfScope, ResultErrorCodes.Forbidden);
        if (command.Panels.Count == 0)
            return Result<PageLayoutDto>.Failure("Bố cục phải có ít nhất một Panel.", ResultErrorCodes.Validation);

        var page = await _pages.GetAsync(command.PageId, ct);
        if (page is null)
            return Result<PageLayoutDto>.Failure($"Page {command.PageId} không tồn tại.", ResultErrorCodes.NotFound);

        var panels = command.Panels.Select((item, index) =>
        {
            var order = item.ReadingOrder is > 0 ? item.ReadingOrder.Value : index + 1;
            var number = item.PanelNumber is > 0 ? item.PanelNumber.Value : index + 1;
            var panel = Panel.Create(command.PageId, number, order, item.BoundingBox ?? "{}", direction: item.Direction);
            panel.CreatedBy = command.UserId.ToString();
            return panel;
        }).OrderBy(p => p.ReadingOrder).ToList();

        if (panels.Select(p => p.PanelNumber).Distinct().Count() != panels.Count ||
            panels.Select(p => p.ReadingOrder).Distinct().Count() != panels.Count)
            return Result<PageLayoutDto>.Failure("PanelNumber và ReadingOrder không được trùng nhau.", ResultErrorCodes.Validation);

        var replacement = await _replacer.ReplaceAsync(page, panels, command.UserId, ct);
        return Result<PageLayoutDto>.Success(new PageLayoutDto(
            replacement.Panels.Select(p => new PanelDto(
                p.Id, p.PageId, p.PanelNumber, p.BoundingBox, p.ReadingOrder, p.CurrentStageId,
                p.CreatedAt, p.UpdatedAt, p.CreatedBy, p.SceneId, p.Version, p.Direction)).ToList(),
            replacement.UnassignedArtworkCount));
    }
}
