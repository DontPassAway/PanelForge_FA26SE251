using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common.Interfaces.Authentication;
using PanelForge.Domain.Common;
using PanelForge.Domain.Entities.Identity;
using PanelForge.Domain.Enums;

namespace PanelForge.Infrastructure.Persistence.Seeders;

/// <summary>
/// Seed tài khoản dùng chung cho team khi chạy local (CHỈ gọi ở môi trường Development).
/// Idempotent theo email: tài khoản đã tồn tại thì bỏ qua, không ghi đè.
/// Mọi tài khoản có mật khẩu <see cref="DefaultPassword"/>, email đã xác thực, 2FA tắt.
/// </summary>
public static class DevUserSeeder
{
    public const string DefaultPassword = "Bnt123#";

    private sealed record SeedUser(
        Guid Id,
        string Email,
        string FullName,
        string? PhoneNumber,
        SystemRole Role = SystemRole.User,
        bool CanCreateStudio = false,
        string? FirebaseUid = null,
        string? AvatarUrl = null);

    private static readonly SeedUser[] Users =
    [
        new(Guid.Parse("11111111-1111-1111-1111-111111111111"), "admin@panelforge.com", "System Administrator", "0900000001", SystemRole.Admin),
        new(Guid.Parse("22222222-2222-2222-2222-222222222222"), "moderator@panelforge.com", "System Moderator", "0900000002"),
        new(Guid.Parse("33333333-3333-3333-3333-333333333333"), "producer@panelforge.com", "Alex Producer", "0900000003", CanCreateStudio: true),
        new(Guid.Parse("44444444-4444-4444-4444-444444444444"), "writer@panelforge.com", "Brian Writer", "0900000004"),
        new(Guid.Parse("55555555-5555-5555-5555-555555555555"), "artist@panelforge.com", "Chloe Artist", "0900000005"),
        new(Guid.Parse("66666666-6666-6666-6666-666666666666"), "letterer@panelforge.com", "David Letterer", "0900000006"),
        new(Guid.Parse("77777777-7777-7777-7777-777777777777"), "editor@panelforge.com", "Emma Editor", "0900000007"),
        new(Guid.Parse("88888888-8888-8888-8888-888888888888"), "reader@panelforge.com", "Frank Reader", "0900000008"),
        new(Guid.Parse("99999999-9999-9999-9999-999999999999"), "publisher@panelforge.com", "Grace Publisher", "0900000009"),
        new(Guid.Parse("78780b6e-314f-4cdd-b813-2cbf24da0697"), "nguyentotrungkien.game@gmail.com", "Nguyen To Trung Kien", "0974867471"),
    ];

    public static async Task SeedAsync(PanelForgeDbContext dbContext, IPasswordHasher passwordHasher, CancellationToken ct = default)
    {
        var existingEmails = await dbContext.Users.Select(u => u.Email).ToListAsync(ct);
        var missing = Users.Where(s => !existingEmails.Contains(s.Email)).ToList();
        if (missing.Count == 0) return;

        var passwordHash = passwordHasher.HashPassword(DefaultPassword);

        foreach (var s in missing)
        {
            var user = User.Create(s.Email, s.FullName, passwordHash, s.FirebaseUid, s.PhoneNumber, s.AvatarUrl, s.Role);
            // Giữ Id cố định để dữ liệu test/Postman của team dùng chung được.
            typeof(BaseEntity).GetProperty(nameof(BaseEntity.Id))!.SetValue(user, s.Id);
            user.ConfirmEmail();
            if (s.CanCreateStudio) user.GrantStudioCreation();
            dbContext.Users.Add(user);
        }

        await dbContext.SaveChangesAsync(ct);
    }
}
