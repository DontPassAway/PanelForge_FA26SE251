using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.Admin.Commands.RestoreEmailSettings;
using PanelForge.Application.Features.Admin.Commands.SendTestEmail;
using PanelForge.Application.Features.Admin.Commands.UpdateEmailSettings;
using PanelForge.Application.Features.Admin.Queries.GetEmailSettings;
using PanelForge.Application.Features.Admin.Queries.GetEmailSettingsHistory;
using PanelForge.Domain.Entities.Identity;
using PanelForge.Domain.Enums;
using PanelForge.Infrastructure.Persistence;
using PanelForge.UnitTests.Common;
using Xunit;

namespace PanelForge.UnitTests.Application.Admin;

/// <summary>
/// Admin cấu hình SMTP gửi email hệ thống trong app (BR-18 audit, append-only): mật khẩu luôn mã hoá, không trả ra API,
/// để trống thì giữ mật khẩu cũ; email thử báo lỗi khi SMTP không gửi được.
/// </summary>
public class PlatformEmailSettingsTests
{
    private readonly PanelForgeDbContext _db = InMemoryDb.Create();
    private readonly Mock<IAiKeyProtector> _protector = new();

    public PlatformEmailSettingsTests()
    {
        _protector.Setup(p => p.Protect(It.IsAny<string>())).Returns<string>(raw => "enc:" + raw);
    }

    private User Admin()
    {
        var admin = User.Create("admin@panelforge.vn", "Quản Trị", role: SystemRole.Admin);
        _db.Users.Add(admin);
        _db.SaveChanges();
        return admin;
    }

    private UpdateEmailSettingsCommandHandler Update() => new(_db, _protector.Object);

    private static UpdateEmailSettingsCommand Command(Guid adminId, string? password, string sender = "noreply@panelforge.vn")
        => new(adminId, " smtp.gmail.com ", 587, "PanelForge", sender, password);

    [Fact]
    public async Task Get_WhenNothingSaved_ReportsNotConfigured()
    {
        var result = await new GetEmailSettingsQueryHandler(_db).Handle(new GetEmailSettingsQuery(), CancellationToken.None);

        result.Value!.IsConfigured.Should().BeFalse();
        result.Value.HasPassword.Should().BeFalse();
    }

    [Fact]
    public async Task Update_FirstTimeWithoutPassword_Fails()
    {
        var admin = Admin();

        var result = await Update().Handle(Command(admin.Id, password: "  "), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ResultErrorCodes.Validation);
        _db.PlatformEmailSettings.Should().BeEmpty();
    }

    [Fact]
    public async Task Update_StoresEncryptedPassword_AndWritesAudit()
    {
        var admin = Admin();

        var result = await Update().Handle(Command(admin.Id, password: "app-password"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.IsConfigured.Should().BeTrue();
        result.Value.SmtpServer.Should().Be("smtp.gmail.com");
        result.Value.HasPassword.Should().BeTrue();
        result.Value.ChangedByName.Should().Be("Quản Trị");

        var saved = await _db.PlatformEmailSettings.SingleAsync();
        saved.ProtectedPassword.Should().Be("enc:app-password");
        var audit = await _db.AuditLogs.SingleAsync(a => a.Action == "PLATFORM_EMAIL_SETTINGS_CHANGED");
        audit.Details.Should().NotContain("app-password");
    }

    [Fact]
    public async Task Update_WithBlankPassword_KeepsPreviousPassword_AppendOnly()
    {
        var admin = Admin();
        await Update().Handle(Command(admin.Id, password: "first-secret"), CancellationToken.None);

        var result = await Update().Handle(Command(admin.Id, password: null, sender: "mail@panelforge.vn"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.SenderEmail.Should().Be("mail@panelforge.vn");
        var rows = await _db.PlatformEmailSettings.OrderBy(s => s.CreatedAt).ToListAsync();
        rows.Should().HaveCount(2);
        rows.Select(r => r.ProtectedPassword).Should().AllBe("enc:first-secret");
    }

    [Theory]
    [InlineData("not-an-email", 587)]
    [InlineData("noreply@panelforge.vn", 0)]
    [InlineData("noreply@panelforge.vn", 70000)]
    public async Task Update_RejectsInvalidValues(string sender, int port)
    {
        var admin = Admin();

        var result = await Update().Handle(
            new UpdateEmailSettingsCommand(admin.Id, "smtp.gmail.com", port, "PanelForge", sender, "secret"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ResultErrorCodes.Validation);
        _db.PlatformEmailSettings.Should().BeEmpty();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SendTestEmail_SendsToAdmin_AndReportsDeliveryFailure(bool delivered)
    {
        var admin = Admin();
        var email = new Mock<IEmailService>();
        email.Setup(e => e.SendEmailAsync(admin.Email, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(delivered);

        var result = await new SendTestEmailCommandHandler(_db, email.Object)
            .Handle(new SendTestEmailCommand(admin.Id), CancellationToken.None);

        result.IsSuccess.Should().Be(delivered);
        if (delivered)
            result.Value!.SentTo.Should().Be(admin.Email);
        else
            result.ErrorCode.Should().Be(ResultErrorCodes.EmailDeliveryFailed);
    }

    [Fact]
    public async Task Restore_OldVersion_AppendsCopyWithItsPassword_AndAudits()
    {
        var admin = Admin();
        await Update().Handle(Command(admin.Id, password: "old-secret", sender: "old@panelforge.vn"), CancellationToken.None);
        await Update().Handle(Command(admin.Id, password: "new-secret", sender: "new@panelforge.vn"), CancellationToken.None);

        var history = (await new GetEmailSettingsHistoryQueryHandler(_db)
            .Handle(new GetEmailSettingsHistoryQuery(), CancellationToken.None)).Value!;
        history.Select(v => v.SenderEmail).Should().Equal("new@panelforge.vn", "old@panelforge.vn");
        history.Select(v => v.IsCurrent).Should().Equal(true, false);

        var result = await new RestoreEmailSettingsCommandHandler(_db)
            .Handle(new RestoreEmailSettingsCommand(admin.Id, history[1].Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.SenderEmail.Should().Be("old@panelforge.vn");
        var rows = await _db.PlatformEmailSettings.OrderBy(s => s.CreatedAt).ToListAsync();
        rows.Should().HaveCount(3);
        rows[^1].ProtectedPassword.Should().Be("enc:old-secret");
        (await _db.AuditLogs.AnyAsync(a => a.Action == "PLATFORM_EMAIL_SETTINGS_RESTORED")).Should().BeTrue();
    }

    [Fact]
    public async Task Restore_CurrentOrUnknownVersion_IsRejected()
    {
        var admin = Admin();
        var saved = await Update().Handle(Command(admin.Id, password: "secret"), CancellationToken.None);
        var currentId = (await _db.PlatformEmailSettings.SingleAsync()).Id;
        var handler = new RestoreEmailSettingsCommandHandler(_db);

        var current = await handler.Handle(new RestoreEmailSettingsCommand(admin.Id, currentId), CancellationToken.None);
        var unknown = await handler.Handle(new RestoreEmailSettingsCommand(admin.Id, Guid.NewGuid()), CancellationToken.None);

        saved.IsSuccess.Should().BeTrue();
        current.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        unknown.ErrorCode.Should().Be(ResultErrorCodes.NotFound);
        _db.PlatformEmailSettings.Should().HaveCount(1);
    }
}
