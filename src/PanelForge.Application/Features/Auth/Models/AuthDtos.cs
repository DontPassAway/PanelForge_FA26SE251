using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Auth.Models;

public sealed record WorkspaceSummaryDto(
    Guid WorkspaceId,
    string Name,
    string Role);

public sealed record LandingContextDto(
    Guid UserId,
    string SystemRole,
    bool CanCreateStudio,
    IReadOnlyCollection<WorkspaceSummaryDto> Workspaces,
    string SuggestedLanding,
    bool TwoFactorEnabled = false,
    int TwoFactorRecoveryCodesLeft = 0);

/// <summary>POST /api/Auth/2fa/recovery-codes: Code là mã 6 số hoặc một mã khôi phục còn hiệu lực.</summary>
public sealed record RegenerateRecoveryCodesRequest(string Code);

public sealed record RecoveryCodesResponse(IReadOnlyList<string> RecoveryCodes);
