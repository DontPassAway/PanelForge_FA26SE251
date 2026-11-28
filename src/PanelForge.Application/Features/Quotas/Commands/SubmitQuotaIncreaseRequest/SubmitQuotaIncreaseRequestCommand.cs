using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Quotas.Common;
using PanelForge.Application.Features.Quotas.Models;
using PanelForge.Application.Features.Quotas.Queries.GetQuotaIncreaseRequests;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Entities.Workspaces;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Quotas.Commands.SubmitQuotaIncreaseRequest;

/// <summary>
/// POST /api/workspaces/{id}/quota-requests — Producer xin Admin tăng một loại hạn mức (OPS-21).
/// Quyền Producer kiểm ở <c>RequireWorkspaceRole</c>; mỗi Studio một yêu cầu đang chờ cho mỗi loại (409 nếu trùng).
/// Mọi Admin đang hoạt động nhận thông báo cùng lần lưu.
/// </summary>
public sealed record SubmitQuotaIncreaseRequestCommand(
    Guid WorkspaceId, Guid ProducerId, QuotaResourceType ResourceType, long RequestedQuota, string Reason)
    : IRequest<Result<QuotaIncreaseRequestDto>>;

public sealed class SubmitQuotaIncreaseRequestCommandHandler(IPanelForgeDbContext db)
    : IRequestHandler<SubmitQuotaIncreaseRequestCommand, Result<QuotaIncreaseRequestDto>>
{
    public async Task<Result<QuotaIncreaseRequestDto>> Handle(SubmitQuotaIncreaseRequestCommand command, CancellationToken ct)
    {
        var workspace = await db.StudioWorkspaces.FirstOrDefaultAsync(w => w.Id == command.WorkspaceId, ct);
        if (workspace is null)
            return Result<QuotaIncreaseRequestDto>.Failure("Không tìm thấy Studio.", ResultErrorCodes.NotFound);

        var ai = await WorkspaceQuotas.LoadAiConfigAsync(db, workspace.Id, ct);
        if (!Enum.IsDefined(command.ResourceType))
            return Result<QuotaIncreaseRequestDto>.Failure("Loại hạn mức không hợp lệ.", ResultErrorCodes.Validation);
        // Studio chưa có cấu hình AI = chưa được cấp AI: việc bật AI do Admin quyết, không qua yêu cầu tăng
        if (WorkspaceQuotas.Current(command.ResourceType, workspace, ai) is not { } current)
            return Result<QuotaIncreaseRequestDto>.Failure(
                "Studio chưa được cấp AI. Liên hệ Admin để bật AI trước.", ResultErrorCodes.Validation);

        var hasPending = await db.QuotaIncreaseRequests.AnyAsync(r =>
            r.WorkspaceId == workspace.Id && r.ResourceType == command.ResourceType
            && r.Status == QuotaIncreaseRequestStatus.Pending, ct);
        if (hasPending)
            return Result<QuotaIncreaseRequestDto>.Failure(
                "Studio đã có một yêu cầu tăng hạn mức loại này đang chờ duyệt.", ResultErrorCodes.Conflict);

        QuotaIncreaseRequest request;
        try
        {
            request = QuotaIncreaseRequest.Submit(
                workspace.Id, command.ResourceType, current, command.RequestedQuota, command.Reason, command.ProducerId);
        }
        catch (ArgumentException ex)
        {
            return Result<QuotaIncreaseRequestDto>.Failure(ex.Message, ResultErrorCodes.Validation);
        }

        db.QuotaIncreaseRequests.Add(request);

        var adminIds = await db.Users
            .Where(u => u.Role == SystemRole.Admin && u.IsActive)
            .Select(u => u.Id)
            .ToListAsync(ct);
        foreach (var adminId in adminIds)
        {
            db.Notifications.Add(Notification.Create(
                adminId,
                NotificationType.QuotaIncreaseRequested,
                "Yêu cầu tăng hạn mức",
                $"Studio \"{workspace.Name}\" xin tăng {QuotaLabels.Of(command.ResourceType)}."));
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex is not DbUpdateConcurrencyException)
        {
            // Hai lần gửi cùng lúc vượt qua kiểm tra trên: chỉ mục unique ux_quota_increase_requests_pending chặn bản sau
            return Result<QuotaIncreaseRequestDto>.Failure(
                "Studio đã có một yêu cầu tăng hạn mức loại này đang chờ duyệt.", ResultErrorCodes.Conflict);
        }

        var dto = await GetQuotaIncreaseRequestsQueryHandler
            .ToDtos(db, db.QuotaIncreaseRequests.AsNoTracking().Where(r => r.Id == request.Id))
            .FirstAsync(ct);
        return Result<QuotaIncreaseRequestDto>.Success(dto);
    }
}
