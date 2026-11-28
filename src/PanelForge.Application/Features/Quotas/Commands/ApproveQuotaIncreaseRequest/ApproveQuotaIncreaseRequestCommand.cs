using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Quotas.Common;
using PanelForge.Application.Features.Quotas.Models;
using PanelForge.Application.Features.Quotas.Queries.GetQuotaIncreaseRequests;
using PanelForge.Application.Features.StudioResources.Common;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Quotas.Commands.ApproveQuotaIncreaseRequest;

/// <summary>
/// POST /api/admin/quota-requests/{id}/approve — Admin duyệt và cấp mức mới cho Studio (OPS-21).
/// Đổi hạn mức, đóng yêu cầu và báo Producer trong cùng một lần lưu (audit tự ghi ở SaveChanges, BR-18).
/// Hai Admin duyệt cùng lúc: khoá xmin của yêu cầu → người sau nhận 409.
/// </summary>
public sealed record ApproveQuotaIncreaseRequestCommand(Guid RequestId, Guid AdminId, long? GrantedQuota, string? Note)
    : IRequest<Result<QuotaIncreaseRequestDto>>;

public sealed class ApproveQuotaIncreaseRequestCommandHandler(IPanelForgeDbContext db)
    : IRequestHandler<ApproveQuotaIncreaseRequestCommand, Result<QuotaIncreaseRequestDto>>
{
    public async Task<Result<QuotaIncreaseRequestDto>> Handle(ApproveQuotaIncreaseRequestCommand command, CancellationToken ct)
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
            request.Approve(command.AdminId, command.GrantedQuota, command.Note);
        }
        catch (ArgumentException ex)
        {
            return Result<QuotaIncreaseRequestDto>.Failure(ex.Message, ResultErrorCodes.Validation);
        }

        var ai = await WorkspaceQuotas.LoadAiConfigAsync(db, request.WorkspaceId, ct);
        ai = WorkspaceQuotas.Apply(db, request.ResourceType, request.GrantedQuota!.Value, request.Workspace, ai);
        if (request.ResourceType is QuotaResourceType.Storage or QuotaResourceType.AiTokens)
        {
            var poolError = await StudioResourceLedger.CheckAdminQuotaWithinPoolAsync(
                db, request.Workspace, request.Workspace.StorageQuotaBytes, ai?.MonthlyTokenQuota ?? 0, ct);
            if (poolError is not null)
                return Result<QuotaIncreaseRequestDto>.Failure(poolError, ResultErrorCodes.InsufficientStudioResources);
        }

        db.Notifications.Add(Notification.Create(
            request.RequestedByUserId,
            NotificationType.QuotaIncreaseApproved,
            "Đã duyệt tăng hạn mức",
            $"Admin đã duyệt tăng {QuotaLabels.Of(request.ResourceType)} cho Studio \"{request.Workspace.Name}\"."));

        await db.SaveChangesAsync(ct);

        return Result<QuotaIncreaseRequestDto>.Success(await GetQuotaIncreaseRequestsQueryHandler
            .ToDtos(db, db.QuotaIncreaseRequests.AsNoTracking().Where(r => r.Id == request.Id))
            .FirstAsync(ct));
    }
}
