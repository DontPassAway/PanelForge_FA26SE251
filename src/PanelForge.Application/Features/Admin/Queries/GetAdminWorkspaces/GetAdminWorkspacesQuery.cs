using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Admin.Models;
using PanelForge.Application.Common.Interfaces.Persistence;

namespace PanelForge.Application.Features.Admin.Queries.GetAdminWorkspaces;

/// <summary>
/// GET /api/admin/workspaces — Danh sách toàn bộ Studio Workspaces trong hệ thống kèm thông số lưu trữ và thành viên (BR-16).
/// </summary>
public sealed record GetAdminWorkspacesQuery : IRequest<Result<IReadOnlyList<AdminWorkspaceDto>>>;

public sealed class GetAdminWorkspacesQueryHandler : IRequestHandler<GetAdminWorkspacesQuery, Result<IReadOnlyList<AdminWorkspaceDto>>>
{
    private readonly IPanelForgeDbContext _dbContext;

    public GetAdminWorkspacesQueryHandler(IPanelForgeDbContext dbContext) => _dbContext = dbContext;

    public async Task<Result<IReadOnlyList<AdminWorkspaceDto>>> Handle(GetAdminWorkspacesQuery query, CancellationToken cancellationToken)
    {
        var workspaces = await _dbContext.StudioWorkspaces
            .AsNoTracking()
            .Include(w => w.Owner)
            .Include(w => w.Members.Where(m => !m.IsDeleted))
            .Include(w => w.Series.Where(s => !s.IsDeleted))
            .Where(w => !w.IsDeleted)
            .OrderByDescending(w => w.CreatedAt)
            .ToListAsync(cancellationToken);

        IReadOnlyList<AdminWorkspaceDto> dtos = workspaces.Select(w => new AdminWorkspaceDto(
            Id: w.Id,
            Name: w.Name,
            OwnerId: w.OwnerId,
            OwnerName: w.Owner != null ? w.Owner.FullName : string.Empty,
            OwnerEmail: w.Owner != null ? w.Owner.Email : string.Empty,
            StorageQuotaBytes: w.StorageQuotaBytes,
            UsedStorageBytes: w.UsedStorageBytes,
            MemberCount: w.Members.Count(m => !m.IsDeleted),
            SeriesCount: w.Series.Count(s => !s.IsDeleted),
            CreatedAt: w.CreatedAt
        )).ToList();

        return Result<IReadOnlyList<AdminWorkspaceDto>>.Success(dtos);
    }
}
