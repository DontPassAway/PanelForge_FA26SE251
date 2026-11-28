using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Resources.Common;
using PanelForge.Domain.Entities.Audit;
using PanelForge.Domain.Entities.Workspaces;

namespace PanelForge.Application.Features.Resources.Commands.ReviewResourceRequest;

/// <summary>
/// POST /api/admin/resource-requests/{id}/approve|reject — Admin xử lý yêu cầu tăng tài nguyên.
/// Duyệt áp hạn mức mới và đổi trạng thái trong CÙNG một SaveChanges; xmin của yêu cầu chặn duyệt hai lần đồng thời.
/// </summary>
public sealed record ReviewResourceRequestCommand(Guid RequestId, Guid AdminId, bool Approve, string? Note)
    : IRequest<Result<bool>>;

public sealed class ReviewResourceRequestCommandHandler : IRequestHandler<ReviewResourceRequestCommand, Result<bool>>
{
    private readonly IPanelForgeDbContext _db;
    public ReviewResourceRequestCommandHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<bool>> Handle(ReviewResourceRequestCommand cmd, CancellationToken ct)
    {
        var request = await _db.ResourceRequests.FirstOrDefaultAsync(r => r.Id == cmd.RequestId, ct);
        if (request is null)
            return Result<bool>.Failure("Không tìm thấy yêu cầu.", ResultErrorCodes.NotFound);
        if (!cmd.Approve && string.IsNullOrWhiteSpace(cmd.Note))
            return Result<bool>.Failure("Từ chối phải ghi lý do.");

        try
        {
            if (cmd.Approve)
            {
                var workspace = await _db.StudioWorkspaces.FirstOrDefaultAsync(w => w.Id == request.WorkspaceId, ct);
                if (workspace is null)
                    return Result<bool>.Failure("Studio của yêu cầu không còn tồn tại.", ResultErrorCodes.NotFound);

                var ai = await _db.WorkspaceAiConfigs.FirstOrDefaultAsync(c => c.WorkspaceId == workspace.Id, ct);
                if (ai is null)
                {
                    ai = ResourceAllocation.NewAiConfig(workspace.Id, await ResourceAllocation.GetPolicyAsync(_db, ct));
                    _db.WorkspaceAiConfigs.Add(ai);
                }

                request.Approve(cmd.AdminId, cmd.Note);
                var previous = ResourceAllocation.CurrentLimit(request.ResourceType, workspace, ai);
                ResourceAllocation.Apply(request.ResourceType, request.RequestedLimit, workspace, ai);

                _db.AuditLogs.Add(AuditLog.Create(
                    "RESOURCE_REQUEST_APPROVED", nameof(ResourceRequest), request.Id.ToString(), workspace.Id, cmd.AdminId,
                    changesJson: JsonSerializer.Serialize(new
                    {
                        ResourceType = request.ResourceType.ToString(),
                        old = previous,
                        @new = request.RequestedLimit
                    }),
                    details: request.ReviewNote ?? request.Reason));
            }
            else
            {
                request.Reject(cmd.AdminId, cmd.Note ?? string.Empty);
                _db.AuditLogs.Add(AuditLog.Create(
                    "RESOURCE_REQUEST_REJECTED", nameof(ResourceRequest), request.Id.ToString(), request.WorkspaceId, cmd.AdminId,
                    details: request.ReviewNote));
            }
        }
        catch (ArgumentException ex)
        {
            return Result<bool>.Failure(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return Result<bool>.Failure(ex.Message, ResultErrorCodes.Conflict);
        }

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<bool>.Failure("Yêu cầu vừa được xử lý bởi Administrator khác.", ResultErrorCodes.Conflict);
        }
        return Result<bool>.Success(true);
    }
}
