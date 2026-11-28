using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Payments.Models;

namespace PanelForge.Application.Features.Payments.Queries.GetPlatformBankAccount;

/// <summary>Tài khoản ngân hàng nền tảng đang dùng và lịch sử thay đổi (Q13, BR-32).</summary>
public sealed record GetPlatformBankAccountQuery(int HistoryLimit = 20) : IRequest<Result<PlatformBankAccountResponseDto>>;

public sealed class GetPlatformBankAccountQueryHandler : IRequestHandler<GetPlatformBankAccountQuery, Result<PlatformBankAccountResponseDto>>
{
    private readonly IPanelForgeDbContext _db;
    private readonly DefaultBankSettings _defaultBank;

    public GetPlatformBankAccountQueryHandler(IPanelForgeDbContext db, DefaultBankSettings defaultBank)
    {
        _db = db;
        _defaultBank = defaultBank;
    }

    public async Task<Result<PlatformBankAccountResponseDto>> Handle(GetPlatformBankAccountQuery q, CancellationToken ct)
    {
        var history = await _db.PlatformBankAccounts
            .AsNoTracking()
            .Where(a => !a.IsDeleted)
            .OrderByDescending(a => a.CreatedAt)
            .Take(Math.Clamp(q.HistoryLimit, 1, 100))
            .Select(a => new PlatformBankAccountDto(
                a.BankBin,
                a.BankName,
                a.AccountNo,
                a.AccountName,
                true,
                a.CreatedAt,
                _db.Users.Where(u => u.Id == a.ChangedByUserId).Select(u => u.FullName).FirstOrDefault()))
            .ToListAsync(ct);

        var current = history.FirstOrDefault() ?? new PlatformBankAccountDto(
            _defaultBank.Bin, _defaultBank.Name, _defaultBank.AccountNo, _defaultBank.AccountName,
            IsConfigured: false, ChangedAt: null, ChangedByName: null);

        return Result<PlatformBankAccountResponseDto>.Success(new PlatformBankAccountResponseDto(current, history));
    }
}
