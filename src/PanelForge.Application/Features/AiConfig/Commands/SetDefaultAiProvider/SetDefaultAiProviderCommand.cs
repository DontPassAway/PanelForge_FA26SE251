using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;

namespace PanelForge.Application.Features.AiConfig.Commands.SetDefaultAiProvider;

/// <summary>
/// TASK-355: Admin chọn API mặc định (chữ hoặc ảnh) cho mọi Studio chưa tự chọn API.
/// ProviderCredentialId null = bỏ mặc định loại đó. Chỉ một API mặc định mỗi loại (unique index lọc).
/// </summary>
public sealed record SetDefaultAiProviderCommand(string Kind, Guid? ProviderCredentialId) : IRequest<Result<bool>>;

public sealed class SetDefaultAiProviderCommandHandler(IPanelForgeDbContext db)
    : IRequestHandler<SetDefaultAiProviderCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(SetDefaultAiProviderCommand command, CancellationToken ct)
    {
        bool? image = command.Kind?.Trim().ToLowerInvariant() switch { "text" => false, "image" => true, _ => null };
        if (image is not { } isImage)
            return Result<bool>.Failure("Kind phải là 'text' hoặc 'image'.", ResultErrorCodes.Validation);

        var target = command.ProviderCredentialId is { } id
            ? await db.AiProviderCredentials.FirstOrDefaultAsync(c => c.Id == id, ct)
            : null;
        if (command.ProviderCredentialId is not null && (target is null || !target.IsEnabled))
            return Result<bool>.Failure("API AI không tồn tại hoặc đang tắt.", ResultErrorCodes.Validation);
        if (isImage && target is { ImageModel: null })
            return Result<bool>.Failure("API này chưa có model sinh ảnh.", ResultErrorCodes.Validation);

        // Bỏ mặc định cũ và lưu trước, rồi mới bật cái mới: Postgres kiểm unique index theo từng dòng.
        var current = await db.AiProviderCredentials
            .Where(c => isImage ? c.IsDefaultImage : c.IsDefaultText).ToListAsync(ct);
        foreach (var c in current.Where(c => c.Id != target?.Id))
            if (isImage) c.SetDefaultImage(false); else c.SetDefaultText(false);
        await db.SaveChangesAsync(ct);

        if (target is not null)
        {
            if (isImage) target.SetDefaultImage(true); else target.SetDefaultText(true);
            await db.SaveChangesAsync(ct);
        }
        return Result<bool>.Success(true);
    }
}
