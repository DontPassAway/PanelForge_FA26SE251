using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Payments.Models;

namespace PanelForge.Application.Features.Payments.Commands.UpdateWorkspaceBankSettings;

public sealed record UpdateWorkspaceBankSettingsCommand(
    Guid WorkspaceId,
    string? BankBin,
    string? BankName,
    string? BankAccountNo,
    string? BankAccountName
) : IRequest<Result<WorkspaceBankSettingsDto>>;

public sealed class UpdateWorkspaceBankSettingsCommandHandler : IRequestHandler<UpdateWorkspaceBankSettingsCommand, Result<WorkspaceBankSettingsDto>>
{
    private readonly IPanelForgeDbContext _db;

    public UpdateWorkspaceBankSettingsCommandHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<WorkspaceBankSettingsDto>> Handle(UpdateWorkspaceBankSettingsCommand command, CancellationToken ct)
    {
        var ws = await _db.StudioWorkspaces
            .FirstOrDefaultAsync(w => w.Id == command.WorkspaceId && !w.IsDeleted, ct);

        if (ws == null)
            return Result<WorkspaceBankSettingsDto>.Failure("Không tìm thấy Workspace.", ResultErrorCodes.NotFound);

        ws.UpdateBankInfo(
            bankBin: command.BankBin,
            bankName: command.BankName,
            bankAccountNo: command.BankAccountNo,
            bankAccountName: command.BankAccountName
        );

        await _db.SaveChangesAsync(ct);

        return Result<WorkspaceBankSettingsDto>.Success(new WorkspaceBankSettingsDto(
            WorkspaceId: ws.Id,
            BankBin: ws.BankBin,
            BankName: ws.BankName,
            BankAccountNo: ws.BankAccountNo,
            BankAccountName: ws.BankAccountName
        ));
    }
}

