using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Resources.Common;
using PanelForge.Application.Features.Resources.Models;
using PanelForge.Domain.Entities.Workspaces;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Resources.Commands.SubmitResourceRequest;

/// <summary>
/// POST /api/workspaces/{id}/resource-requests — Producer xin tăng tài nguyên (chỉ cấp phát, không thanh toán).
/// Quyền Producer kiểm ở controller (RequireWorkspaceRole). Mỗi loại tài nguyên chỉ có một yêu cầu Pending.
/// </summary>
public sealed record SubmitResourceRequestCommand(Guid WorkspaceId, Guid UserId, SubmitResourceRequestRequest Request)
    : IRequest<Result<Guid>>;

public sealed class SubmitResourceRequestCommandHandler : IRequestHandler<SubmitResourceRequestCommand, Result<Guid>>
{
    private readonly IPanelForgeDbContext _db;
    public SubmitResourceRequestCommandHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<Guid>> Handle(SubmitResourceRequestCommand cmd, CancellationToken ct)
    {
        var workspace = await _db.StudioWorkspaces.AsNoTracking().FirstOrDefaultAsync(w => w.Id == cmd.WorkspaceId, ct);
        if (workspace is null)
            return Result<Guid>.Failure("Không tìm thấy Studio.", ResultErrorCodes.NotFound);

        var type = cmd.Request.ResourceType;
        if (await _db.ResourceRequests.AnyAsync(r => r.WorkspaceId == workspace.Id && r.ResourceType == type
                                                     && r.Status == ResourceRequestStatus.Pending, ct))
            return Result<Guid>.Failure("Đã có yêu cầu đang chờ duyệt cho loại tài nguyên này.", ResultErrorCodes.Conflict);

        var ai = await _db.WorkspaceAiConfigs.AsNoTracking().FirstOrDefaultAsync(c => c.WorkspaceId == workspace.Id, ct);

        if (!Enum.IsDefined(type))
            return Result<Guid>.Failure("Loại tài nguyên không hợp lệ.");
        if (string.IsNullOrWhiteSpace(cmd.Request.Reason))
            return Result<Guid>.Failure("Vui lòng nhập lý do.");
        var current = ResourceAllocation.CurrentLimit(type, workspace, ai);
        if (current == 0)
            return Result<Guid>.Failure("Studio đang không giới hạn tài nguyên này.");
        if (cmd.Request.RequestedLimit <= current)
            return Result<Guid>.Failure("Mức yêu cầu phải lớn hơn mức hiện tại.");

        ResourceRequest request;
        try
        {
            request = ResourceRequest.Create(workspace.Id, type, current, cmd.Request.RequestedLimit, cmd.Request.Reason, cmd.UserId);
        }
        catch (ArgumentException ex)
        {
            return Result<Guid>.Failure(ex.Message);
        }

        _db.ResourceRequests.Add(request);
        await _db.SaveChangesAsync(ct);
        return Result<Guid>.Success(request.Id);
    }
}
