using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.StudioResources.Common;
using PanelForge.Application.Features.StudioResources.Models;
using PanelForge.Application.Features.Workspaces.Models;
using PanelForge.Domain.Entities.Audit;
using PanelForge.Domain.Entities.Workspaces;

namespace PanelForge.Application.Features.StudioResources.Commands.AllocateStudioResources;

/// <summary>
/// PUT /api/workspaces/{id}/resources — chủ Studio chia lại dung lượng và token AI từ kho của mình (D12).
/// Chỉ chủ sở hữu (không phải mọi Producer được mời); không thấp hơn dung lượng đã dùng; tổng không vượt kho.
/// </summary>
public sealed record AllocateStudioResourcesCommand(
    Guid UserId,
    Guid WorkspaceId,
    long StorageQuotaBytes,
    long TokenQuota) : IRequest<Result<StudioResourcesDto>>;

public sealed class AllocateStudioResourcesCommandHandler
    : IRequestHandler<AllocateStudioResourcesCommand, Result<StudioResourcesDto>>
{
    private readonly IPanelForgeDbContext _db;
    private readonly WorkspaceDefaultsSettings _defaults;

    public AllocateStudioResourcesCommandHandler(IPanelForgeDbContext db, WorkspaceDefaultsSettings defaults)
    {
        _db = db;
        _defaults = defaults;
    }

    public async Task<Result<StudioResourcesDto>> Handle(AllocateStudioResourcesCommand command, CancellationToken ct)
    {
        if (command.StorageQuotaBytes <= 0 || command.TokenQuota <= 0)
            return Failure("Dung lượng và token AI chia cho Studio phải lớn hơn 0.", ResultErrorCodes.Validation);

        var workspace = await _db.StudioWorkspaces
            .FirstOrDefaultAsync(w => w.Id == command.WorkspaceId && !w.IsDeleted, ct);
        if (workspace is null)
            return Failure("Không tìm thấy Studio.", ResultErrorCodes.NotFound);
        if (workspace.OwnerId != command.UserId)
            return Failure("Chỉ chủ Studio mới chia được tài nguyên cho Studio này.", ResultErrorCodes.Forbidden);

        var pool = await _db.StudioResourcePools
            .FirstOrDefaultAsync(p => p.OwnerId == command.UserId && !p.IsDeleted, ct);
        if (pool is null)
            return Failure("Bạn chưa có kho tài nguyên. Liên hệ Administrator để mua dung lượng và token AI.",
                ResultErrorCodes.Validation);

        if (command.StorageQuotaBytes < workspace.UsedStorageBytes)
            return Failure(
                $"Studio đang dùng {StudioResourceLedger.FormatBytes(workspace.UsedStorageBytes)}, không chia thấp hơn mức này được.",
                ResultErrorCodes.Validation);

        var aiConfig = await _db.WorkspaceAiConfigs.FirstOrDefaultAsync(c => c.WorkspaceId == workspace.Id, ct);
        if (aiConfig is { IsPrepaidTokenBudget: true } && command.TokenQuota < aiConfig.UsedTokensCurrentMonth)
            return Failure(
                $"Studio đã dùng {aiConfig.UsedTokensCurrentMonth:N0} token, không chia thấp hơn mức này được.",
                ResultErrorCodes.Validation);

        var others = (await StudioResourceLedger.OwnedStudiosAsync(_db, command.UserId, ct))
            .Where(s => s.WorkspaceId != workspace.Id)
            .ToList();
        var storageLeft = pool.StorageBytes - others.Sum(s => s.StorageQuotaBytes);
        var tokensLeft = pool.Tokens - others.Sum(s => s.TokenQuota);
        if (command.StorageQuotaBytes > storageLeft || command.TokenQuota > tokensLeft)
            return Failure(
                $"Kho không đủ: chia tối đa {StudioResourceLedger.FormatBytes(Math.Max(0, storageLeft))} và " +
                $"{Math.Max(0, tokensLeft):N0} token cho Studio này.",
                ResultErrorCodes.InsufficientStudioResources);

        if (aiConfig is null)
        {
            aiConfig = WorkspaceAiConfig.Create(workspace.Id, monthlyTokenQuota: command.TokenQuota);
            _db.WorkspaceAiConfigs.Add(aiConfig);
        }

        var before = $"{StudioResourceLedger.FormatBytes(workspace.StorageQuotaBytes)}, {aiConfig.MonthlyTokenQuota:N0} token";
        workspace.SetStorageQuota(command.StorageQuotaBytes);
        aiConfig.UsePrepaidTokenBudget(command.TokenQuota);
        pool.MarkAllocationChanged();

        _db.AuditLogs.Add(AuditLog.Create(
            action: "STUDIO_RESOURCES_ALLOCATED",
            entityName: nameof(StudioWorkspace),
            entityId: workspace.Id.ToString(),
            userId: command.UserId,
            details: $"Chia lại tài nguyên cho Studio \"{workspace.Name}\": {before} → " +
                     $"{StudioResourceLedger.FormatBytes(command.StorageQuotaBytes)}, {command.TokenQuota:N0} token"));

        // xmin của kho: hai lần chia song song → lần sau 409 (ConcurrencyConflictFilter), không cùng vượt kho.
        await _db.SaveChangesAsync(ct);

        return Result<StudioResourcesDto>.Success(await StudioResourceLedger.LoadAsync(_db, _defaults, command.UserId, ct));
    }

    private static Result<StudioResourcesDto> Failure(string message, string code)
        => Result<StudioResourcesDto>.Failure(message, code);
}
