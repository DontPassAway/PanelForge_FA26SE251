using MediatR;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Assignments.Common;
using PanelForge.Application.Features.Assignments.Models;

namespace PanelForge.Application.Features.Assignments.Commands.ReopenChangeRequest;

/// <summary>CF3 bước 8: Editor thấy chưa đạt nên mở lại (Resolved → Open), comment bắt buộc, thông báo người chịu trách nhiệm.</summary>
public sealed record ReopenChangeRequestCommand(Guid ChapterId, Guid ChangeRequestId, Guid UserId, string? Comment)
    : IRequest<Result<ChangeRequestDto>>;

public sealed class ReopenChangeRequestCommandHandler : IRequestHandler<ReopenChangeRequestCommand, Result<ChangeRequestDto>>
{
    private readonly IPanelForgeDbContext _db;

    public ReopenChangeRequestCommandHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<ChangeRequestDto>> Handle(ReopenChangeRequestCommand cmd, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cmd.Comment))
            return Result<ChangeRequestDto>.Failure("Mở lại bắt buộc phải có comment.", ResultErrorCodes.Validation);

        var loaded = await ChangeRequestRules.LoadAsync(_db, cmd.ChapterId, cmd.ChangeRequestId, ct);
        if (!loaded.IsSuccess)
            return Result<ChangeRequestDto>.Failure(loaded.ErrorMessage!, loaded.ErrorCode);

        var cr = loaded.Value!;
        if (cr.ResponsibleUserId == cmd.UserId)
            return Result<ChangeRequestDto>.Failure("Không thể tự mở lại yêu cầu sửa của chính mình.", ResultErrorCodes.Forbidden);

        try { cr.Reopen(cmd.Comment); }
        catch (InvalidOperationException ex) { return Result<ChangeRequestDto>.Failure(ex.Message, ResultErrorCodes.Conflict); }

        cr.UpdatedBy = cmd.UserId.ToString();
        _db.Notifications.Add(AssignmentNotifications.ChangeRequestReopened(cr));

        await _db.SaveChangesAsync(ct);
        return Result<ChangeRequestDto>.Success(ChangeRequestDto.From(cr));
    }
}
