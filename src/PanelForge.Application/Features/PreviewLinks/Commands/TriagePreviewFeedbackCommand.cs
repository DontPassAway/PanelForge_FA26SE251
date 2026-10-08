using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.PreviewLinks.Models;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.PreviewLinks.Commands;

/// <summary>
/// UC-24: Producer/Editor phân loại phản hồi: Noted (ghi chú cho bản sau) hoặc NeedsChange (cần sửa, note bắt buộc).
/// NeedsChange không tự mở lại chương: Producer mở lại bằng API reopen (lý do có thể lấy từ note); khi mở lại, mọi link bị thu hồi.
/// </summary>
public sealed record TriagePreviewFeedbackCommand(
    Guid ChapterId, Guid FeedbackId, Guid UserId, PreviewFeedbackStatus Decision, string? Note)
    : IRequest<Result<PreviewFeedbackDto>>;

public sealed class TriagePreviewFeedbackCommandHandler : IRequestHandler<TriagePreviewFeedbackCommand, Result<PreviewFeedbackDto>>
{
    private readonly IPanelForgeDbContext _db;

    public TriagePreviewFeedbackCommandHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<PreviewFeedbackDto>> Handle(TriagePreviewFeedbackCommand cmd, CancellationToken ct)
    {
        var feedback = await _db.PreviewFeedback.FirstOrDefaultAsync(f => f.Id == cmd.FeedbackId && f.ChapterId == cmd.ChapterId, ct);
        if (feedback is null) return Result<PreviewFeedbackDto>.Failure("Phản hồi không tồn tại.", ResultErrorCodes.NotFound);

        try { feedback.Triage(cmd.Decision, cmd.Note, cmd.UserId); }
        catch (ArgumentException ex) { return Result<PreviewFeedbackDto>.Failure(ex.Message, ResultErrorCodes.Validation); }

        feedback.UpdatedBy = cmd.UserId.ToString();
        await _db.SaveChangesAsync(ct);
        return Result<PreviewFeedbackDto>.Success(PreviewFeedbackDto.From(feedback));
    }
}
