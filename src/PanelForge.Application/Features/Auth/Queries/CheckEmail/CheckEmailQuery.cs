using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Auth.Common;

namespace PanelForge.Application.Features.Auth.Queries.CheckEmail;

/// <summary>Luồng Email-First của Mobile: email đã có tài khoản chưa (để chọn bước Đăng nhập hay Đăng ký).</summary>
public sealed record CheckEmailQuery(string Email) : IRequest<Result<CheckEmailResponse>>;

public sealed record CheckEmailResponse(bool Exists);

public sealed class CheckEmailQueryHandler : IRequestHandler<CheckEmailQuery, Result<CheckEmailResponse>>
{
    private readonly IPanelForgeDbContext _dbContext;

    public CheckEmailQueryHandler(IPanelForgeDbContext dbContext) => _dbContext = dbContext;

    public async Task<Result<CheckEmailResponse>> Handle(CheckEmailQuery query, CancellationToken ct)
    {
        var email = AuthSecrets.NormalizeEmail(query.Email ?? string.Empty);
        if (email.Length == 0 || email.Length > 256 || !email.Contains('@'))
            return Result<CheckEmailResponse>.Failure("Email không hợp lệ.");

        var exists = await _dbContext.Users.AnyAsync(u => u.Email == email, ct);
        return Result<CheckEmailResponse>.Success(new CheckEmailResponse(exists));
    }
}
