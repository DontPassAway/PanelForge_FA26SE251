using MediatR;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Assignments.Common;
using PanelForge.Application.Features.Assignments.Models;

namespace PanelForge.Application.Features.Assignments.Commands.ResolveChangeRequest;

/// <summary>CF3 bước 8: người chịu trách nhiệm báo đã sửa (Open → Resolved) và thông báo người tạo yêu cầu.</summary>
public sealed record ResolveChangeRequestCommand(Guid ChapterId, Guid ChangeRequestId, Guid UserId, string? Note)
    : IRequest<Result<ChangeRequestDto>>;

public sealed class ResolveChangeRequestCommandHandler : IRequestHandler<ResolveChangeRequestCommand, Result<ChangeRequestDto>>
{
    private readonly IPanelForgeDbContext _db;

    public ResolveChangeRequestCommandHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<ChangeRequestDto>> Handle(ResolveChangeRequestCommand cmd, CancellationToken ct)
    {
        var loaded = await ChangeRequestRules.LoadAsync(_db, cmd.ChapterId, cmd.ChangeRequestId, ct);
        if (!loaded.IsSuccess)
            return Result<ChangeRequestDto>.Failure(loaded.ErrorMessage!, loaded.ErrorCode);

        var cr = loaded.Value!;
        if (cr.ResponsibleUserId != cmd.UserId)
            return Result<ChangeRequestDto>.Failure("Chỉ người chịu trách nhiệm mới đánh dấu đã sửa.", ResultErrorCodes.Forbidden);

        try { cr.Resolve(cmd.Note); }
        catch (InvalidOperationException ex) { return Result<ChangeRequestDto>.Failure(ex.Message, ResultErrorCodes.Conflict); }

        cr.UpdatedBy = cmd.UserId.ToString();
        _db.Notifications.Add(AssignmentNotifications.ChangeRequestResolved(cr));

        await _db.SaveChangesAsync(ct);
        return Result<ChangeRequestDto>.Success(ChangeRequestDto.From(cr));
    }
}
