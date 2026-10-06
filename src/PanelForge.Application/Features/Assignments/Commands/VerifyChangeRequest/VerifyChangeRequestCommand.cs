using MediatR;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Assignments.Common;
using PanelForge.Application.Features.Assignments.Models;

namespace PanelForge.Application.Features.Assignments.Commands.VerifyChangeRequest;

/// <summary>CF3 bước 8: Editor xác minh đã đạt (Resolved → Verified). Người xác minh phải khác người chịu trách nhiệm.</summary>
public sealed record VerifyChangeRequestCommand(Guid ChapterId, Guid ChangeRequestId, Guid UserId)
    : IRequest<Result<ChangeRequestDto>>;

public sealed class VerifyChangeRequestCommandHandler : IRequestHandler<VerifyChangeRequestCommand, Result<ChangeRequestDto>>
{
    private readonly IPanelForgeDbContext _db;

    public VerifyChangeRequestCommandHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<ChangeRequestDto>> Handle(VerifyChangeRequestCommand cmd, CancellationToken ct)
    {
        var loaded = await ChangeRequestRules.LoadAsync(_db, cmd.ChapterId, cmd.ChangeRequestId, ct);
        if (!loaded.IsSuccess)
            return Result<ChangeRequestDto>.Failure(loaded.ErrorMessage!, loaded.ErrorCode);

        var cr = loaded.Value!;
        if (cr.ResponsibleUserId == cmd.UserId)
            return Result<ChangeRequestDto>.Failure("Không thể tự xác minh yêu cầu sửa của chính mình.", ResultErrorCodes.Forbidden);

        try { cr.Verify(cmd.UserId); }
        catch (InvalidOperationException ex) { return Result<ChangeRequestDto>.Failure(ex.Message, ResultErrorCodes.Conflict); }

        cr.UpdatedBy = cmd.UserId.ToString();
        await _db.SaveChangesAsync(ct);
        return Result<ChangeRequestDto>.Success(ChangeRequestDto.From(cr));
    }
}
