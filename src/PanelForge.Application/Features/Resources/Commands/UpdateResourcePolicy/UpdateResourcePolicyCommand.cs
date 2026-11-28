using MediatR;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Resources.Common;
using PanelForge.Application.Features.Resources.Models;
using PanelForge.Domain.Entities.Workspaces;

namespace PanelForge.Application.Features.Resources.Commands.UpdateResourcePolicy;

/// <summary>
/// PUT /api/admin/resource-policy — đổi chính sách mặc định. Chỉ áp cho Studio tạo sau đó;
/// quota của Studio đã có không bị đổi. Audit tự động (CREATE/UPDATE_RESOURCEPOLICY kèm old/new).
/// </summary>
public sealed record UpdateResourcePolicyCommand(UpdateResourcePolicyRequest Request) : IRequest<Result<ResourcePolicyDto>>;

public sealed class UpdateResourcePolicyCommandHandler : IRequestHandler<UpdateResourcePolicyCommand, Result<ResourcePolicyDto>>
{
    private readonly IPanelForgeDbContext _db;
    public UpdateResourcePolicyCommandHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<ResourcePolicyDto>> Handle(UpdateResourcePolicyCommand cmd, CancellationToken ct)
    {
        var r = cmd.Request;
        if (r.DefaultStorageQuotaBytes < 0 || r.DefaultMonthlyTokenQuota < 0 || r.DefaultMonthlyImageQuota < 0)
            return Result<ResourcePolicyDto>.Failure("Hạn mức không được là số âm.");
        if (r.WarningThresholdPercent is < 1 or > 99)
            return Result<ResourcePolicyDto>.Failure("Ngưỡng cảnh báo phải từ 1 đến 99%.");

        var saved = await ResourceAllocation.FindSavedPolicyAsync(_db, ct);
        var policy = saved ?? ResourcePolicy.CreateDefault();
        try
        {
            policy.Update(r.DefaultStorageQuotaBytes, r.DefaultMonthlyTokenQuota, r.DefaultMonthlyImageQuota,
                r.DefaultAiEnabled, r.WarningThresholdPercent);
        }
        catch (ArgumentException ex)
        {
            return Result<ResourcePolicyDto>.Failure(ex.Message);
        }

        if (saved is null) _db.ResourcePolicies.Add(policy);
        await _db.SaveChangesAsync(ct);
        return Result<ResourcePolicyDto>.Success(ResourceAllocation.ToDto(policy, true));
    }
}
