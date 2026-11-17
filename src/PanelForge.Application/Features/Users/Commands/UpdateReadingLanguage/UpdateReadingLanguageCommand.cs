using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Domain.Common;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Users.Commands.UpdateReadingLanguage;

/// <summary>
/// BR-34: Cập nhật ngôn ngữ đọc ưa thích của người dùng.
/// </summary>
public sealed record UpdateReadingLanguageCommand(
    Guid UserId,
    string? LanguageCode
) : IRequest<Result<string?>>;

public sealed class UpdateReadingLanguageCommandHandler
    : IRequestHandler<UpdateReadingLanguageCommand, Result<string?>>
{
    private readonly IPanelForgeDbContext _db;

    public UpdateReadingLanguageCommandHandler(IPanelForgeDbContext db)
    {
        _db = db;
    }

    public async Task<Result<string?>> Handle(
        UpdateReadingLanguageCommand cmd, CancellationToken ct)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == cmd.UserId, ct);
        if (user is null)
            return Result<string?>.Failure($"Người dùng {cmd.UserId} không tồn tại.", ResultErrorCodes.NotFound);

        if (!string.IsNullOrWhiteSpace(cmd.LanguageCode))
        {
            var norm = ContentLanguages.Normalize(cmd.LanguageCode);
            if (norm is null)
                return Result<string?>.Failure($"Mã ngôn ngữ '{cmd.LanguageCode}' không hợp lệ.", ResultErrorCodes.Validation);
            user.SetPreferredReadingLanguage(norm);
        }
        else
        {
            user.SetPreferredReadingLanguage(null);
        }

        await _db.SaveChangesAsync(ct);
        return Result<string?>.Success(user.PreferredReadingLanguage);
    }
}
