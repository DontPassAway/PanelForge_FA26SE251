using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Payments.Models;

namespace PanelForge.Application.Features.Payments.Queries.GetWorkspaceBankSettings;

public sealed record GetWorkspaceBankSettingsQuery(Guid WorkspaceId) : IRequest<Result<WorkspaceBankSettingsDto>>;

public sealed class GetWorkspaceBankSettingsQueryHandler : IRequestHandler<GetWorkspaceBankSettingsQuery, Result<WorkspaceBankSettingsDto>>
{
    private readonly IPanelForgeDbContext _db;
    private readonly DefaultBankSettings _defaultBank;

    public GetWorkspaceBankSettingsQueryHandler(IPanelForgeDbContext db, DefaultBankSettings defaultBank)
    {
        _db = db;
        _defaultBank = defaultBank;
    }

    public async Task<Result<WorkspaceBankSettingsDto>> Handle(GetWorkspaceBankSettingsQuery q, CancellationToken ct)
    {
        var ws = await _db.StudioWorkspaces
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == q.WorkspaceId && !w.IsDeleted, ct);

        if (ws == null)
            return Result<WorkspaceBankSettingsDto>.Failure("Không tìm thấy Workspace.", ResultErrorCodes.NotFound);

        return Result<WorkspaceBankSettingsDto>.Success(new WorkspaceBankSettingsDto(
            WorkspaceId: ws.Id,
            BankBin: ws.BankBin ?? _defaultBank.Bin,
            BankName: ws.BankName ?? _defaultBank.Name,
            BankAccountNo: ws.BankAccountNo ?? _defaultBank.AccountNo,
            BankAccountName: ws.BankAccountName ?? _defaultBank.AccountName
        ));
    }
}

