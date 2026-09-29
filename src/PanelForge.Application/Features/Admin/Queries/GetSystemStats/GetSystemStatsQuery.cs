using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Admin.Models;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Admin.Queries.GetSystemStats;

/// <summary>GET /api/admin/system-stats — số liệu tổng quan cho Admin dashboard.</summary>
public sealed record GetSystemStatsQuery : IRequest<Result<SystemStatsDto>>;

public sealed class GetSystemStatsQueryHandler : IRequestHandler<GetSystemStatsQuery, Result<SystemStatsDto>>
{
    private readonly IPanelForgeDbContext _dbContext;

    public GetSystemStatsQueryHandler(IPanelForgeDbContext dbContext) => _dbContext = dbContext;

    public async Task<Result<SystemStatsDto>> Handle(GetSystemStatsQuery query, CancellationToken cancellationToken)
    {
        var totalUsers     = await _dbContext.Users.CountAsync(cancellationToken);
        var adminCount     = await _dbContext.Users.CountAsync(u => u.Role == SystemRole.Admin, cancellationToken);
        var userCount      = await _dbContext.Users.CountAsync(u => u.Role == SystemRole.User, cancellationToken);
        var workspaceCount = await _dbContext.StudioWorkspaces.CountAsync(cancellationToken);
        var memberCount    = await _dbContext.WorkspaceMembers.CountAsync(cancellationToken);

        return Result<SystemStatsDto>.Success(new SystemStatsDto(
            totalUsers, adminCount, userCount, workspaceCount, memberCount, DateTime.UtcNow));
    }
}
