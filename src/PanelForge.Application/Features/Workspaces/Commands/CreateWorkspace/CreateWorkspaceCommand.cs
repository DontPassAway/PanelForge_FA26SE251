using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Quotas.Common;
using PanelForge.Application.Features.StudioResources.Common;
using PanelForge.Application.Features.Workspaces.Models;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Domain.Entities.Identity;
using PanelForge.Domain.Entities.Workspaces;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Workspaces.Commands.CreateWorkspace;

/// <summary>
/// POST /api/workspaces — tạo Studio. Chỉ User có CanCreateStudio (BR-22), không phải Admin (BR-07).
/// Người tạo tự động là Producer trong workspace_members. Hạn mức (dung lượng, token và ảnh AI) lấy từ chính sách
/// Admin đang dùng, chưa có thì từ <see cref="WorkspaceDefaultsSettings"/>; không nhận từ client (OPS-01, OPS-20).
/// D12: Producer có kho tài nguyên thì Studio mới lấy phần chia từ kho (client chọn, mặc định = hạn mức chính sách);
/// kho không đủ → INSUFFICIENT_STUDIO_RESOURCES (409). Token chia từ kho là token mua một lần, không reset theo tháng.
/// </summary>
public sealed record CreateWorkspaceCommand(Guid OwnerId, string Name, long? StorageQuotaBytes = null, long? TokenQuota = null)
    : IRequest<Result<WorkspaceDto>>;

public sealed class CreateWorkspaceCommandHandler : IRequestHandler<CreateWorkspaceCommand, Result<WorkspaceDto>>
{
    private readonly IPanelForgeDbContext _dbContext;
    private readonly WorkspaceDefaultsSettings _defaults;

    public CreateWorkspaceCommandHandler(IPanelForgeDbContext dbContext, WorkspaceDefaultsSettings defaults)
    {
        _dbContext = dbContext;
        _defaults = defaults;
    }

    public async Task<Result<WorkspaceDto>> Handle(CreateWorkspaceCommand command, CancellationToken cancellationToken)
    {
        var owner = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == command.OwnerId, cancellationToken);
        if (owner is null || !owner.IsActive)
            return Unauthorized("Người dùng không tồn tại hoặc đã bị vô hiệu hóa.");

        if (owner.Role == SystemRole.Admin)
            return Unauthorized("Administrator không được phép tạo Studio (BR-07).");

        if (!owner.CanCreateStudio)
            return Unauthorized("Bạn chưa có quyền tạo Studio. Mua gói mở Studio trong Ví hoặc nhờ Administrator cấp quyền.");

        var policy = await QuotaPolicies.GetEffectiveAsync(_dbContext, _defaults, cancellationToken);
        var storageQuota = policy.StorageQuotaBytes;
        var tokenQuota = policy.MonthlyTokenQuota;

        var pool = await _dbContext.StudioResourcePools
            .FirstOrDefaultAsync(p => p.OwnerId == owner.Id && !p.IsDeleted, cancellationToken);
        if (pool is not null)
        {
            storageQuota = command.StorageQuotaBytes ?? policy.StorageQuotaBytes;
            tokenQuota = command.TokenQuota ?? policy.MonthlyTokenQuota;
            if (storageQuota <= 0 || tokenQuota <= 0)
                return Result<WorkspaceDto>.Failure(
                    "Dung lượng và token AI chia cho Studio mới phải lớn hơn 0.", ResultErrorCodes.Validation);

            var owned = await StudioResourceLedger.OwnedStudiosAsync(_dbContext, owner.Id, cancellationToken);
            var storageLeft = Math.Max(0, pool.StorageBytes - owned.Sum(s => s.StorageQuotaBytes));
            var tokensLeft = Math.Max(0, pool.Tokens - owned.Sum(s => s.TokenQuota));
            if (storageQuota > storageLeft || tokenQuota > tokensLeft)
                return Result<WorkspaceDto>.Failure(
                    $"Kho tài nguyên không đủ để tạo Studio: còn {StudioResourceLedger.FormatBytes(storageLeft)} và " +
                    $"{tokensLeft:N0} token. Giảm phần chia, thu bớt từ Studio khác hoặc mua thêm.",
                    ResultErrorCodes.InsufficientStudioResources);

            // xmin của kho: hai lần tạo Studio song song không cùng vượt kho
            pool.MarkAllocationChanged();
        }

        StudioWorkspace workspace;
        try
        {
            workspace = StudioWorkspace.Create(command.Name, owner.Id, storageQuota);
        }
        catch (ArgumentException ex)
        {
            return Result<WorkspaceDto>.Failure(ex.Message);
        }

        _dbContext.StudioWorkspaces.Add(workspace);
        _dbContext.WorkspaceMembers.Add(WorkspaceMember.Create(workspace.Id, owner.Id, WorkspaceRole.Producer));
        // TASK-355: Studio mới có sẵn cấu hình AI mặc định → dùng ngay API mặc định Admin đã đặt
        var aiConfig = WorkspaceAiConfig.Create(workspace.Id, monthlyTokenQuota: policy.MonthlyTokenQuota);
        aiConfig.SetMonthlyImageQuota(policy.MonthlyImageQuota);
        aiConfig.SetRequestLimits(policy.MonthlyRequestQuota, policy.RequestsPerMinute);
        if (!policy.AiEnabledByDefault)
            aiConfig.UpdateSettings(aiConfig.Provider, null, false, policy.MonthlyTokenQuota, string.Empty);
        if (pool is not null)
            aiConfig.UsePrepaidTokenBudget(tokenQuota);
        _dbContext.WorkspaceAiConfigs.Add(aiConfig);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result<WorkspaceDto>.Success(new WorkspaceDto(
            Id: workspace.Id,
            Name: workspace.Name,
            OwnerId: owner.Id,
            OwnerName: owner.FullName,
            OwnerEmail: owner.Email,
            StorageQuotaBytes: workspace.StorageQuotaBytes,
            UsedStorageBytes: workspace.UsedStorageBytes,
            CreatedAt: workspace.CreatedAt,
            IsOwner: true,
            CurrentUserRole: WorkspaceRole.Producer,
            MemberCount: 1));
    }

    private static Result<WorkspaceDto> Unauthorized(string message)
        => Result<WorkspaceDto>.Failure(message, ResultErrorCodes.Unauthorized);
}
