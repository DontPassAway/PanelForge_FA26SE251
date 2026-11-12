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

    public GetWorkspaceBankSettingsQueryHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<WorkspaceBankSettingsDto>> Handle(GetWorkspaceBankSettingsQuery q, CancellationToken ct)
    {
        var ws = await _db.StudioWorkspaces
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == q.WorkspaceId && !w.IsDeleted, ct);

        if (ws == null)
            return Result<WorkspaceBankSettingsDto>.Failure("Không tìm thấy Workspace.", ResultErrorCodes.NotFound);

        return Result<WorkspaceBankSettingsDto>.Success(new WorkspaceBankSettingsDto(
            WorkspaceId: ws.Id,
            BankBin: ws.BankBin ?? "970422",
            BankName: ws.BankName ?? "MB Bank",
            BankAccountNo: ws.BankAccountNo ?? "0123456789",
            BankAccountName: ws.BankAccountName ?? "KOMAFORGE STUDIO"
        ));
    }
}

