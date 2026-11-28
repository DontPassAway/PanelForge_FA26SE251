using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.Admin.Commands.ConfirmUserEmailByAdmin;
using PanelForge.Application.Features.Admin.Commands.ResendUserVerificationByAdmin;
using PanelForge.Domain.Entities.Identity;
using PanelForge.Infrastructure.Persistence;
using PanelForge.UnitTests.Common;
using Xunit;

namespace PanelForge.UnitTests.Application.Admin;

/// <summary>
/// Admin xử lý tài khoản chưa xác thực email: gửi lại OTP hoặc xác thực thủ công kèm lý do.
/// Mọi thao tác ghi audit log (BR-18); tài khoản đang chờ lời mời Studio không được xử lý ở đây.
/// </summary>
public class AdminEmailVerificationTests
{
    private readonly PanelForgeDbContext _db = InMemoryDb.Create();
    private readonly Mock<IEmailService> _email = new();
    private readonly Guid _adminId = Guid.NewGuid();

    private User Unverified(bool withPassword = true)
    {
        var user = User.Create("u@test.com", "Người Dùng", passwordHash: withPassword ? "hash" : null);
        _db.Users.Add(user);
        _db.SaveChanges();
        return user;
    }

    private void EmailSends(bool ok) => _email
        .Setup(e => e.SendEmailVerificationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(ok);

    private Task<Result<PanelForge.Application.Features.Admin.Models.AdminUserStatusDto>> Resend(Guid userId)
        => new ResendUserVerificationByAdminCommandHandler(_db, _email.Object)
            .Handle(new ResendUserVerificationByAdminCommand(_adminId, userId), CancellationToken.None);

    private Task<Result<PanelForge.Application.Features.Admin.Models.AdminUserStatusDto>> Confirm(Guid userId, string reason)
        => new ConfirmUserEmailByAdminCommandHandler(_db)
            .Handle(new ConfirmUserEmailByAdminCommand(_adminId, userId, reason), CancellationToken.None);

    [Fact]
    public async Task Resend_Unverified_SetsNewOtpSendsEmailAndAudits()
    {
        var user = Unverified();
        EmailSends(true);

        var result = await Resend(user.Id);

        result.IsSuccess.Should().BeTrue();
        user.EmailVerificationToken.Should().NotBeNullOrEmpty();
        user.EmailVerificationTokenExpiresAt.Should().BeAfter(DateTime.UtcNow);
        _email.Verify(e => e.SendEmailVerificationAsync("u@test.com", user.EmailVerificationToken!, "Người Dùng",
            It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Once);
        (await _db.AuditLogs.SingleAsync(a => a.Action == "ADMIN_RESEND_EMAIL_VERIFICATION"))
            .UserId.Should().Be(_adminId);
    }

    [Fact]
    public async Task Resend_WhenSmtpFails_ReturnsDeliveryFailedAndAuditsFailure()
    {
        var user = Unverified();
        EmailSends(false);

        var result = await Resend(user.Id);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ResultErrorCodes.EmailDeliveryFailed);
        (await _db.AuditLogs.AnyAsync(a => a.Action == "ADMIN_RESEND_EMAIL_VERIFICATION_FAILED")).Should().BeTrue();
    }

    [Fact]
    public async Task Resend_AlreadyVerified_IsConflictAndSendsNothing()
    {
        var user = Unverified();
        user.ConfirmEmail();
        await _db.SaveChangesAsync();

        var result = await Resend(user.Id);

        result.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        _email.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Resend_PendingInvitation_IsConflict()
    {
        var user = Unverified(withPassword: false);

        var result = await Resend(user.Id);

        result.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        _email.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Resend_UnknownUser_IsNotFound()
    {
        var result = await Resend(Guid.NewGuid());

        result.ErrorCode.Should().Be(ResultErrorCodes.NotFound);
    }

    [Fact]
    public async Task Confirm_WithReason_ConfirmsClearsOtpAndAuditsReason()
    {
        var user = Unverified();
        user.SetEmailVerificationToken("123456", DateTime.UtcNow.AddMinutes(15));
        await _db.SaveChangesAsync();

        var result = await Confirm(user.Id, "  Người dùng gọi hotline, đã đối chiếu CCCD  ");

        result.IsSuccess.Should().BeTrue();
        user.IsEmailConfirmed.Should().BeTrue();
        user.EmailVerificationToken.Should().BeNull();
        var log = await _db.AuditLogs.SingleAsync(a => a.Action == "ADMIN_CONFIRM_EMAIL");
        log.UserId.Should().Be(_adminId);
        log.EntityId.Should().Be(user.Id.ToString());
        log.Details.Should().Contain("đã đối chiếu CCCD");
    }

    [Fact]
    public async Task Confirm_WithoutReason_Fails()
    {
        var user = Unverified();

        var result = await Confirm(user.Id, "   ");

        result.IsSuccess.Should().BeFalse();
        user.IsEmailConfirmed.Should().BeFalse();
    }

    [Fact]
    public async Task Confirm_PendingInvitation_IsConflict()
    {
        var user = Unverified(withPassword: false);

        var result = await Confirm(user.Id, "lý do");

        result.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        user.IsEmailConfirmed.Should().BeFalse();
    }

    [Fact]
    public async Task Confirm_AlreadyVerified_IsConflict()
    {
        var user = Unverified();
        user.ConfirmEmail();
        await _db.SaveChangesAsync();

        var result = await Confirm(user.Id, "lý do");

        result.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
    }
}
