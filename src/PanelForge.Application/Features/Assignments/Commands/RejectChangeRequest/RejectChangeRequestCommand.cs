using MediatR;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Assignments.Common;
using PanelForge.Application.Features.Assignments.Models;

namespace PanelForge.Application.Features.Assignments.Commands.RejectChangeRequest;

/// <summary>CF3: chấp nhận phản đối, đóng yêu cầu không sửa (Open → Rejected). Lý do bắt buộc, người quyết khác người chịu trách nhiệm.</summary>
public sealed record RejectChangeRequestCommand(Guid ChapterId, Guid ChangeRequestId, Guid UserId, string? Reason)
    : IRequest<Result<ChangeRequestDto>>;

public sealed class RejectChangeRequestCommandHandler : IRequestHandler<RejectChangeRequestCommand, Result<ChangeRequestDto>>
{
    private readonly IPanelForgeDbContext _db;

    public RejectChangeRequestCommandHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<ChangeRequestDto>> Handle(RejectChangeRequestCommand cmd, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cmd.Reason))
            return Result<ChangeRequestDto>.Failure("Từ chối yêu cầu sửa bắt buộc phải có lý do.", ResultErrorCodes.Validation);

        var loaded = await ChangeRequestRules.LoadAsync(_db, cmd.ChapterId, cmd.ChangeRequestId, ct);
        if (!loaded.IsSuccess)
            return Result<ChangeRequestDto>.Failure(loaded.ErrorMessage!, loaded.ErrorCode);

        var cr = loaded.Value!;
        if (cr.ResponsibleUserId == cmd.UserId)
            return Result<ChangeRequestDto>.Failure("Không thể tự đóng yêu cầu sửa của chính mình.", ResultErrorCodes.Forbidden);

        try { cr.Reject(cmd.UserId, cmd.Reason); }
        catch (InvalidOperationException ex) { return Result<ChangeRequestDto>.Failure(ex.Message, ResultErrorCodes.Conflict); }

        cr.UpdatedBy = cmd.UserId.ToString();
        await _db.SaveChangesAsync(ct);
        return Result<ChangeRequestDto>.Success(ChangeRequestDto.From(cr));
    }
}
