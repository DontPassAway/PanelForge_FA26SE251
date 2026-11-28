using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Quotas.Common;
using PanelForge.Application.Features.Quotas.Models;
using PanelForge.Application.Features.Quotas.Queries.GetQuotaIncreaseRequests;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Quotas.Commands.RejectQuotaIncreaseRequest;

/// <summary>POST /api/admin/quota-requests/{id}/reject — Admin từ chối kèm lý do, báo Producer (OPS-21).</summary>
public sealed record RejectQuotaIncreaseRequestCommand(Guid RequestId, Guid AdminId, string Note)
    : IRequest<Result<QuotaIncreaseRequestDto>>;

public sealed class RejectQuotaIncreaseRequestCommandHandler(IPanelForgeDbContext db)
    : IRequestHandler<RejectQuotaIncreaseRequestCommand, Result<QuotaIncreaseRequestDto>>
{
    public async Task<Result<QuotaIncreaseRequestDto>> Handle(RejectQuotaIncreaseRequestCommand command, CancellationToken ct)
    {
        var request = await db.QuotaIncreaseRequests
            .Include(r => r.Workspace)
            .FirstOrDefaultAsync(r => r.Id == command.RequestId, ct);
        if (request is null)
            return Result<QuotaIncreaseRequestDto>.Failure("Không tìm thấy yêu cầu.", ResultErrorCodes.NotFound);
        if (request.Status != QuotaIncreaseRequestStatus.Pending)
            return Result<QuotaIncreaseRequestDto>.Failure("Yêu cầu này đã được xử lý.", ResultErrorCodes.Conflict);

        try
        {
            request.Reject(command.AdminId, command.Note);
        }
        catch (ArgumentException ex)
        {
            return Result<QuotaIncreaseRequestDto>.Failure(ex.Message, ResultErrorCodes.Validation);
        }

        db.Notifications.Add(Notification.Create(
            request.RequestedByUserId,
            NotificationType.QuotaIncreaseRejected,
            "Yêu cầu tăng hạn mức bị từ chối",
            $"Admin từ chối tăng {QuotaLabels.Of(request.ResourceType)} cho Studio \"{request.Workspace.Name}\": {request.ReviewNote}"));

        await db.SaveChangesAsync(ct);

        return Result<QuotaIncreaseRequestDto>.Success(await GetQuotaIncreaseRequestsQueryHandler
            .ToDtos(db, db.QuotaIncreaseRequests.AsNoTracking().Where(r => r.Id == request.Id))
            .FirstAsync(ct));
    }
}
