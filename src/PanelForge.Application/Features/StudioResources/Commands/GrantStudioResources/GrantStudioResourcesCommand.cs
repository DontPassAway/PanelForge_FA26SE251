using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.StudioResources.Common;
using PanelForge.Application.Features.StudioResources.Models;
using PanelForge.Application.Features.Workspaces.Models;
using PanelForge.Domain.Entities.Audit;
using PanelForge.Domain.Entities.Workspaces;

namespace PanelForge.Application.Features.StudioResources.Commands.GrantStudioResources;

/// <summary>
/// POST /api/admin/users/{id}/studio-resources — Admin bán thêm dung lượng / token AI cho Producer (D12): cộng vào kho.
/// Producer chưa có kho thì kho được tạo từ đúng phần các Studio hiện có đang giữ, để phần thêm là phần còn lại để chia.
/// </summary>
public sealed record GrantStudioResourcesCommand(
    Guid AdminUserId,
    Guid ProducerUserId,
    long StorageBytes,
    long Tokens) : IRequest<Result<StudioResourcesDto>>;

public sealed class GrantStudioResourcesCommandHandler
    : IRequestHandler<GrantStudioResourcesCommand, Result<StudioResourcesDto>>
{
    private readonly IPanelForgeDbContext _db;
    private readonly WorkspaceDefaultsSettings _defaults;

    public GrantStudioResourcesCommandHandler(IPanelForgeDbContext db, WorkspaceDefaultsSettings defaults)
    {
        _db = db;
        _defaults = defaults;
    }

    public async Task<Result<StudioResourcesDto>> Handle(GrantStudioResourcesCommand command, CancellationToken ct)
    {
        if (command.StorageBytes < 0 || command.Tokens < 0 || command.StorageBytes + command.Tokens == 0)
            return Result<StudioResourcesDto>.Failure(
                "Nhập dung lượng hoặc số token cần cộng thêm (không âm, ít nhất một giá trị lớn hơn 0).",
                ResultErrorCodes.Validation);

        var producer = await _db.Users.FirstOrDefaultAsync(u => u.Id == command.ProducerUserId, ct);
        if (producer is null)
            return Result<StudioResourcesDto>.Failure("Không tìm thấy người dùng.", ResultErrorCodes.NotFound);
        if (!producer.CanCreateStudio)
            return Result<StudioResourcesDto>.Failure(
                "Người dùng chưa có quyền tạo Studio (chưa mua gói Mở Studio).", ResultErrorCodes.Validation);

        var pool = await StudioResourceLedger.EnsurePoolAsync(_db, producer.Id, ct);
        pool.Add(command.StorageBytes, command.Tokens);
        _db.AuditLogs.Add(AuditLog.Create(
            action: "STUDIO_RESOURCES_GRANTED",
            entityName: nameof(StudioResourcePool),
            entityId: pool.Id.ToString(),
            userId: command.AdminUserId,
            details: $"Cộng cho {producer.Email}: +{StudioResourceLedger.FormatBytes(command.StorageBytes)}, " +
                     $"+{command.Tokens:N0} token"));
        await _db.SaveChangesAsync(ct);

        return Result<StudioResourcesDto>.Success(await StudioResourceLedger.LoadAsync(_db, _defaults, producer.Id, ct));
    }
}
