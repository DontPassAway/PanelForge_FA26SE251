using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.Workspaces.Models;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Workspaces.Queries.GetAutomationSettings;

/// <summary>Assign Automation: Producer/Editor xem cấu hình; chưa có dòng thì trả mặc định (mọi quy tắc tắt).</summary>
public record GetAutomationSettingsQuery(Guid UserId, Guid WorkspaceId) : IRequest<Result<AutomationSettingsDto>>;

public class GetAutomationSettingsQueryHandler : IRequestHandler<GetAutomationSettingsQuery, Result<AutomationSettingsDto>>
{
    private readonly IPanelForgeDbContext _db;
    private readonly IWorkspaceAuthorizationService _auth;

    public GetAutomationSettingsQueryHandler(IPanelForgeDbContext db, IWorkspaceAuthorizationService auth)
    {
        _db = db;
        _auth = auth;
    }

    public async Task<Result<AutomationSettingsDto>> Handle(GetAutomationSettingsQuery query, CancellationToken ct)
    {
        if (!await _auth.HasWorkspaceRoleAsync(query.UserId, query.WorkspaceId, [WorkspaceRole.Producer, WorkspaceRole.Editor], ct))
            return Result<AutomationSettingsDto>.Failure("Chỉ Producer hoặc Editor xem được cấu hình Automation.", ResultErrorCodes.Forbidden);

        var settings = await _db.WorkspaceAutomationSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.WorkspaceId == query.WorkspaceId, ct);
        return Result<AutomationSettingsDto>.Success(
            settings is null ? AutomationSettingsDto.Default(query.WorkspaceId) : AutomationSettingsDto.From(settings));
    }
}