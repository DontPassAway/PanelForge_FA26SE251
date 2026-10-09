using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Admin.Models;
using PanelForge.Application.Common.Interfaces.Persistence;

namespace PanelForge.Application.Features.Admin.Queries.GetAdminUsers;

/// <summary>GET /api/admin/users — danh sách tài khoản, mới nhất trước (UC-01).</summary>
public sealed record GetAdminUsersQuery(int Page = 1, int PageSize = 20)
    : IRequest<Result<AdminUserListResponse>>;

public sealed class GetAdminUsersQueryHandler
    : IRequestHandler<GetAdminUsersQuery, Result<AdminUserListResponse>>
{
    private readonly IPanelForgeDbContext _dbContext;

    public GetAdminUsersQueryHandler(IPanelForgeDbContext dbContext) => _dbContext = dbContext;

    public async Task<Result<AdminUserListResponse>> Handle(
        GetAdminUsersQuery query,
        CancellationToken cancellationToken)
    {
        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = query.PageSize is < 1 or > 100 ? 20 : query.PageSize;

        var total = await _dbContext.Users.CountAsync(cancellationToken);
        var items = await _dbContext.Users
            .OrderByDescending(u => u.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(u => new AdminUserListItemDto(
                u.Id,
                u.Email,
                u.FullName,
                u.PhoneNumber,
                u.Role.ToString(),
                u.CanCreateStudio,
                u.IsActive,
                u.IsEmailConfirmed,
                u.TwoFactorEnabled,
                u.CreatedAt))
            .ToListAsync(cancellationToken);

        return Result<AdminUserListResponse>.Success(new AdminUserListResponse(total, page, pageSize, items));
    }
}
