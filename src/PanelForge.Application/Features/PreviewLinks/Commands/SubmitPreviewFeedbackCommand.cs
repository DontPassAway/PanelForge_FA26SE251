using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.PreviewLinks.Common;
using PanelForge.Application.Features.PreviewLinks.Models;
using PanelForge.Domain.Entities.Preview;

namespace PanelForge.Application.Features.PreviewLinks.Commands;

/// <summary>
/// UC-21: người nhận gửi phản hồi cấp chương / trang / panel, gắn đúng phiên bản đang xem (artwork trang trong snapshot).
/// Không có quyền sản xuất nào (BR-07).
/// </summary>
public sealed record SubmitPreviewFeedbackCommand(string? Token, string? Message, Guid? PageId, Guid? PanelId, string? IpAddress,
    string? ReviewerName = null)
    : IRequest<Result<PreviewFeedbackDto>>;

public sealed class SubmitPreviewFeedbackCommandHandler : IRequestHandler<SubmitPreviewFeedbackCommand, Result<PreviewFeedbackDto>>
{
    private readonly IPanelForgeDbContext _db;

    public SubmitPreviewFeedbackCommandHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<PreviewFeedbackDto>> Handle(SubmitPreviewFeedbackCommand cmd, CancellationToken ct)
    {
        static Result<PreviewFeedbackDto> Fail(string m, string c) => Result<PreviewFeedbackDto>.Failure(m, c);

        var (link, deny) = await PreviewLinkRules.ResolveAsync(_db, cmd.Token, "Feedback", cmd.IpAddress, ct);
        await _db.SaveChangesAsync(ct); // log được lưu cả khi bị từ chối / dữ liệu sai
        if (link is null) return Fail(PreviewLinkRules.Message(deny), ResultErrorCodes.Forbidden);

        if (string.IsNullOrWhiteSpace(cmd.Message))
            return Fail("Phản hồi không được để trống.", ResultErrorCodes.Validation);
        if (cmd.PanelId is not null && cmd.PageId is null)
            return Fail("Phản hồi cấp panel cần kèm pageId.", ResultErrorCodes.Validation);

        // Trang phải thuộc snapshot của link; phiên bản gắn với phản hồi = artwork của trang trong snapshot
        Guid? assetReferenceId = null;
        if (cmd.PageId is { } pageId)
        {
            var snapshotIds = link.SnapshotArtworkIds;
            // Trang ghép: phản hồi cấp panel gắn đúng ảnh của Panel đó; còn lại lấy artwork bất kỳ của trang
            assetReferenceId = await _db.AssetReferences.AsNoTracking()
                .Where(r => snapshotIds.Contains(r.Id) && r.PageId == pageId)
                .OrderByDescending(r => cmd.PanelId != null && r.PanelId == cmd.PanelId)
                .Select(r => (Guid?)r.Id).FirstOrDefaultAsync(ct);
            if (assetReferenceId is null) return Fail("Trang không thuộc bản xem thử này.", ResultErrorCodes.Validation);

            if (cmd.PanelId is { } panelId &&
                !await _db.Panels.AsNoTracking().AnyAsync(p => p.Id == panelId && p.PageId == pageId, ct))
                return Fail("Panel không thuộc trang này.", ResultErrorCodes.Validation);
        }

        PreviewFeedback feedback;
        try { feedback = PreviewFeedback.Create(link, cmd.PageId, cmd.PanelId, assetReferenceId, cmd.Message, cmd.ReviewerName); }
        catch (ArgumentException ex) { return Fail(ex.Message, ResultErrorCodes.Validation); }

        feedback.CreatedBy = link.RecipientEmail;
        _db.PreviewFeedback.Add(feedback);
        await _db.SaveChangesAsync(ct);
        return Result<PreviewFeedbackDto>.Success(PreviewFeedbackDto.From(feedback));
    }
}
