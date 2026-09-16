using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.DTOs.Auth;
using PanelForge.Application.Interfaces.Persistence;
using PanelForge.Domain.Enums;

namespace PanelForge.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin")]
public class AdminController : ControllerBase
{
    private readonly IPanelForgeDbContext _dbContext;

    public AdminController(IPanelForgeDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet("users")]
    public async Task<IActionResult> GetAllUsers(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 100) pageSize = 20;

        var totalUsers = await _dbContext.Users.CountAsync(cancellationToken);
        var users = await _dbContext.Users
            .OrderByDescending(u => u.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(u => new
            {
                u.Id,
                u.Email,
                u.FullName,
                u.PhoneNumber,
                Role = u.Role.ToString(),
                u.IsActive,
                u.IsEmailConfirmed,
                u.TwoFactorEnabled,
                u.CreatedAt
            })
            .ToListAsync(cancellationToken);

        return Ok(new
        {
            Total = totalUsers,
            Page = page,
            PageSize = pageSize,
            Items = users
        });
    }

    [HttpPost("assign-role")]
    public async Task<IActionResult> AssignRole(
        [FromBody] AssignRoleRequest request,
        CancellationToken cancellationToken)
    {
        var user = await _dbContext.Users
            .FirstOrDefaultAsync(u => u.Id == request.UserId, cancellationToken);

        if (user is null)
        {
            return NotFound(new { message = "Không tìm thấy người dùng." });
        }

        user.AssignSystemRole(request.NewRole);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            message = $"Đã cập nhật vai trò của người dùng {user.Email} thành {request.NewRole}.",
            userId = user.Id,
            newRole = user.Role.ToString()
        });
    }

    [HttpGet("system-stats")]
    public async Task<IActionResult> GetSystemStats(CancellationToken cancellationToken)
    {
        var totalUsers = await _dbContext.Users.CountAsync(cancellationToken);
        var adminCount = await _dbContext.Users.CountAsync(u => u.Role == SystemRole.Admin, cancellationToken);
        var modCount = await _dbContext.Users.CountAsync(u => u.Role == SystemRole.Moderator, cancellationToken);
        var userCount = await _dbContext.Users.CountAsync(u => u.Role == SystemRole.User, cancellationToken);

        return Ok(new
        {
            TotalUsers = totalUsers,
            Admins = adminCount,
            Moderators = modCount,
            StandardUsers = userCount,
            ServerTimeUtc = DateTime.UtcNow
        });
    }
}
