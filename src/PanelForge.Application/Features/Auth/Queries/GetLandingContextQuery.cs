using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Interfaces.Persistence;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Auth.Queries;

// ─── BR-23: Landing Context ──────────────────────────────────────────────────

public sealed record WorkspaceSummaryDto(
    Guid WorkspaceId,
    string Name,
    string Role);

public sealed record LandingContextDto(
    Guid UserId,
    string SystemRole,
    bool CanCreateStudio,
    IReadOnlyCollection<WorkspaceSummaryDto> Workspaces,
    string SuggestedLanding);

/// <summary>
/// BR-23: Read authenticated user's identity and workspace memberships to determine
/// the optimal landing screen. UserId is resolved from the HTTP context in the controller,
/// never accepted from the client.
/// </summary>
public sealed record GetLandingContextQuery(Guid UserId) : IRequest<Result<LandingContextDto>>;

public sealed class GetLandingContextQueryHandler
    : IRequestHandler<GetLandingContextQuery, Result<LandingContextDto>>
{
    private readonly IPanelForgeDbContext _dbContext;

    public GetLandingContextQueryHandler(IPanelForgeDbContext dbContext)
        => _dbContext = dbContext;

    public async Task<Result<LandingContextDto>> Handle(
        GetLandingContextQuery query, CancellationToken ct)
    {
        var user = await _dbContext.Users
            .FirstOrDefaultAsync(u => u.Id == query.UserId, ct);

        if (user is null)
            return Result<LandingContextDto>.Failure("Người dùng không tồn tại.");

        // Chỉ lấy các Workspace mà user là thành viên (BR-23 — WorkspaceMember filter)
        var memberships = await _dbContext.WorkspaceMembers
            .Include(m => m.Workspace)
            .Where(m => m.UserId == query.UserId && !m.IsDeleted)
            .OrderBy(m => m.JoinedAt)
            .ToListAsync(ct);

        var workspaces = memberships
            .Select(m => new WorkspaceSummaryDto(
                WorkspaceId: m.WorkspaceId,
                Name: m.Workspace?.Name ?? string.Empty,
                Role: m.Role.ToString()))
            .ToList();

        // BR-23: Routing logic
        var suggestedLanding = DetermineLanding(user.Role, user.CanCreateStudio, workspaces.Count);

        return Result<LandingContextDto>.Success(new LandingContextDto(
            UserId: user.Id,
            SystemRole: user.Role.ToString(),
            CanCreateStudio: user.CanCreateStudio,
            Workspaces: workspaces,
            SuggestedLanding: suggestedLanding));
    }

    private static string DetermineLanding(SystemRole role, bool canCreateStudio, int workspaceCount)
    {
        if (role == SystemRole.Admin)
            return "AdminDashboard";

        if (workspaceCount > 0)
            return "YourWorkspaces";

        if (canCreateStudio)
            return "StudioCreation";

        return "PublicCatalog";
    }
}
