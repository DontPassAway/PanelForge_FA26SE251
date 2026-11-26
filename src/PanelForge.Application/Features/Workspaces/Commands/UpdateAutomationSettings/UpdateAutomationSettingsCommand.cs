using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.Workspaces.Models;
using PanelForge.Domain.Entities.Workspaces;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Workspaces.Commands.UpdateAutomationSettings;

/// <summary>Assign Automation: Producer/Owner lưu cấu hình (upsert). <c>RowVersion</c> lệch thì 409 (BR-11).</summary>
public record UpdateAutomationSettingsCommand(Guid UserId, Guid WorkspaceId, UpdateAutomationSettingsRequest Request)
    : IRequest<Result<AutomationSettingsDto>>;

public class UpdateAutomationSettingsCommandHandler : IRequestHandler<UpdateAutomationSettingsCommand, Result<AutomationSettingsDto>>
{
    private readonly IPanelForgeDbContext _db;
    private readonly IWorkspaceAuthorizationService _auth;

    public UpdateAutomationSettingsCommandHandler(IPanelForgeDbContext db, IWorkspaceAuthorizationService auth)
    {
        _db = db;
        _auth = auth;
    }

    public async Task<Result<AutomationSettingsDto>> Handle(UpdateAutomationSettingsCommand cmd, CancellationToken ct)
    {
        static Result<AutomationSettingsDto> Fail(string msg, string code = ResultErrorCodes.Validation) =>
            Result<AutomationSettingsDto>.Failure(msg, code);

        if (!await _auth.IsOwnerOrProducerAsync(cmd.UserId, cmd.WorkspaceId, ct))
            return Fail("Chỉ Owner hoặc Producer được đổi cấu hình Automation.", ResultErrorCodes.Forbidden);

        var settings = await _db.WorkspaceAutomationSettings.FirstOrDefaultAsync(s => s.WorkspaceId == cmd.WorkspaceId, ct);
        if (settings?.RowVersion != cmd.Request.RowVersion)
            return Fail("Cấu hình Automation vừa được người khác thay đổi. Tải lại rồi thử lại.", ResultErrorCodes.Conflict);

        var isNew = settings is null;
        settings ??= WorkspaceAutomationSettings.Create(cmd.WorkspaceId);
        var r = cmd.Request;
        try { settings.Update(r.AutoNextStage, r.AssignMode, r.AutoRouteReview, r.LeadHours, r.EscalationHours); }
        catch (ArgumentException ex) { return Fail(ex.Message); }

        if (isNew)
        {
            settings.CreatedBy = cmd.UserId.ToString();
            _db.WorkspaceAutomationSettings.Add(settings);
        }
        else settings.UpdatedBy = cmd.UserId.ToString();

        await _db.SaveChangesAsync(ct);
        return Result<AutomationSettingsDto>.Success(AutomationSettingsDto.From(settings));
    }
}