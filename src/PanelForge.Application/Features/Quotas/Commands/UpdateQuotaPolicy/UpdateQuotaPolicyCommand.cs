using MediatR;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Quotas.Common;
using PanelForge.Application.Features.Quotas.Models;
using PanelForge.Application.Features.Workspaces.Models;
using QuotaPolicyEntity = PanelForge.Domain.Entities.Workspaces.QuotaPolicy;

namespace PanelForge.Application.Features.Quotas.Commands.UpdateQuotaPolicy;

/// <summary>
/// PUT /api/admin/quota-policy — Admin đặt hạn mức cho Studio tạo từ nay về sau (OPS-20).
/// Thêm bản ghi mới (append-only), không sửa Studio đã có: mức cấp riêng của từng Studio giữ nguyên.
/// </summary>
public sealed record UpdateQuotaPolicyCommand(
    Guid AdminId, long StorageQuotaBytes, long MonthlyTokenQuota, long MonthlyImageQuota,
    bool AiEnabledByDefault = true, int WarningThresholdPercent = QuotaPolicyEntity.DefaultWarningThresholdPercent,
    long MonthlyRequestQuota = QuotaPolicyEntity.DefaultMonthlyRequestQuota,
    int RequestsPerMinute = QuotaPolicyEntity.DefaultRequestsPerMinute)
    : IRequest<Result<QuotaPolicyDto>>;

public sealed class UpdateQuotaPolicyCommandHandler(IPanelForgeDbContext db, WorkspaceDefaultsSettings defaults)
    : IRequestHandler<UpdateQuotaPolicyCommand, Result<QuotaPolicyDto>>
{
    public async Task<Result<QuotaPolicyDto>> Handle(UpdateQuotaPolicyCommand command, CancellationToken ct)
    {
        if (command.StorageQuotaBytes <= 0 || command.MonthlyTokenQuota <= 0 || command.MonthlyImageQuota <= 0
            || command.MonthlyRequestQuota <= 0 || command.RequestsPerMinute <= 0)
            return Result<QuotaPolicyDto>.Failure(
                "Mọi mức trong chính sách phải lớn hơn 0. Muốn bỏ giới hạn thì sửa riêng từng Studio.",
                ResultErrorCodes.Validation);
        if (command.WarningThresholdPercent is < 1 or > 99)
            return Result<QuotaPolicyDto>.Failure("Ngưỡng cảnh báo phải từ 1 đến 99%.", ResultErrorCodes.Validation);

        db.QuotaPolicies.Add(QuotaPolicyEntity.Create(
            command.StorageQuotaBytes, command.MonthlyTokenQuota, command.MonthlyImageQuota, command.AdminId,
            command.AiEnabledByDefault, command.WarningThresholdPercent,
            command.MonthlyRequestQuota, command.RequestsPerMinute));
        await db.SaveChangesAsync(ct);

        return Result<QuotaPolicyDto>.Success(await QuotaPolicies.GetEffectiveAsync(db, defaults, ct));
    }
}
