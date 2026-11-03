using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Moq;
using OtpNet;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Auth.Commands.ChangePassword;
using PanelForge.Application.Features.Auth.Commands.DisableTwoFactor;
using PanelForge.Application.Features.Auth.Commands.EnableTwoFactor;
using PanelForge.Application.Features.Auth.Common;
using PanelForge.Application.Features.Auth.Commands.ExternalLogin;
using PanelForge.Application.Features.Auth.Commands.ForgetDevice;
using PanelForge.Application.Features.Auth.Commands.ForgotPassword;
using PanelForge.Application.Features.Auth.Commands.Login;
using PanelForge.Application.Features.Auth.Commands.Register;
using PanelForge.Application.Features.Auth.Commands.ResendVerificationEmail;
using PanelForge.Application.Features.Auth.Commands.ResetPassword;
using PanelForge.Application.Features.Auth.Commands.RefreshToken;
using PanelForge.Application.Features.Auth.Commands.VerifyEmail;
using PanelForge.Application.Features.Auth.Commands.VerifyTwoFactor;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Common.Interfaces.Authentication;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Domain.Entities.Identity;
using PanelForge.Infrastructure.Authentication;
using PanelForge.UnitTests.Common;
using Xunit;

namespace PanelForge.UnitTests.Application.Auth;

/// <summary>Các handler thay thế AuthService cũ — giữ nguyên các kịch bản kiểm thử trước đây.</summary>
public class AuthCommandHandlerTests
{
    private readonly Mock<IPasswordHasher> _passwordHasherMock = new();
    private readonly Mock<IJwtTokenGenerator> _jwtTokenGeneratorMock = new();
    private readonly Mock<IPanelForgeDbContext> _dbContextMock = new();
    private readonly Mock<IEmailService> _emailServiceMock = new();
    private readonly ITotpService _totpService = new OtpNetTotpService();
    private readonly List<UserRememberedDevice> _rememberedDevices = new();

    private readonly List<RefreshToken> _refreshTokens = new();
    private readonly List<PanelForge.Domain.Entities.Audit.AuditLog> _auditLogs = new();

    public AuthCommandHandlerTests()
    {
        SetupRememberedDevices(_rememberedDevices);
        _dbContextMock.Setup(db => db.RefreshTokens).Returns(DbSetMockHelper.CreateDbSetMock(_refreshTokens));
        _dbContextMock.Setup(db => db.AuditLogs).Returns(DbSetMockHelper.CreateDbSetMock(_auditLogs));
        // Session policy (V1.2): user chưa thuộc Workspace nào → không bị giới hạn thêm
        _dbContextMock.Setup(db => db.WorkspaceMembers)
            .Returns(DbSetMockHelper.CreateDbSetMock(new List<PanelForge.Domain.Entities.Workspaces.WorkspaceMember>()));
    }

    private void SetupUsers(List<User> users)
    {
        _dbContextMock.Setup(db => db.Users).Returns(DbSetMockHelper.CreateDbSetMock(users));
        _dbContextMock.Setup(db => db.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
    }

    private void SetupRememberedDevices(List<UserRememberedDevice> devices)
        => _dbContextMock.Setup(db => db.UserRememberedDevices).Returns(DbSetMockHelper.CreateDbSetMock(devices));

    private static string HashToken(string token)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    private RegisterCommandHandler Register() => new(_dbContextMock.Object, _passwordHasherMock.Object, _emailServiceMock.Object);
    private LoginCommandHandler Login() => new(_dbContextMock.Object, _passwordHasherMock.Object, _jwtTokenGeneratorMock.Object);
    private ChangePasswordCommandHandler ChangePassword() => new(_dbContextMock.Object, _passwordHasherMock.Object, _totpService);
    private VerifyTwoFactorCommandHandler VerifyTwoFactor() => new(_dbContextMock.Object, _totpService, _jwtTokenGeneratorMock.Object);

    // ══════════════════════════════════════════════════════════════════════════
    // REGISTER
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Register_WhenEmailAlreadyExists_ShouldFail()
    {
        SetupUsers(new List<User> { User.Create("test@example.com", "Existing User", "hashed_pwd") });

        var result = await Register().Handle(
            new RegisterCommand("test@example.com", "Password123!", "New User", null), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().BeNull(); // → 400
        result.ErrorMessage.Should().Be("Email đã được đăng ký trong hệ thống.");
    }

    [Fact]
    public async Task Register_WhenValid_ShouldCreateUserAndSendVerificationEmail()
    {
        var usersList = new List<User>();
        SetupUsers(usersList);
        _passwordHasherMock.Setup(h => h.HashPassword("Password123!")).Returns("hashed_secure_password");

        var result = await Register().Handle(
            new RegisterCommand("tam@example.com", "Password123!", "Bui Ngoc Tam", "0123456789"), CancellationToken.None);

        // Đăng ký chưa phát JWT; phải xác thực email trước.
        var response = result.Value!;
        response.Token.Should().BeNull();
        response.ExpiresAt.Should().BeNull();
        response.User!.Email.Should().Be("tam@example.com");
        response.User.FullName.Should().Be("Bui Ngoc Tam");
        response.Message.Should().Contain("xác thực");

        usersList.Should().ContainSingle(u => u.Email == "tam@example.com");
        _dbContextMock.Verify(db => db.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _emailServiceMock.Verify(e => e.SendEmailVerificationAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // LOGIN
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Login_WhenUserNotFound_ShouldReturnUnauthorized()
    {
        SetupUsers(new List<User>());

        var result = await Login().Handle(
            new LoginCommand("notfound@example.com", "Password123!", null), CancellationToken.None);

        result.ErrorCode.Should().Be(ResultErrorCodes.Unauthorized);
        result.ErrorMessage.Should().Be("Email hoặc mật khẩu không chính xác.");
    }

    [Fact]
    public async Task Login_WhenUserInactive_ShouldReturnUnauthorized()
    {
        var user = User.Create("inactive@example.com", "Inactive User", "hashed_pwd");
        user.Deactivate();
        SetupUsers(new List<User> { user });

        var result = await Login().Handle(
            new LoginCommand("inactive@example.com", "Password123!", null), CancellationToken.None);

        result.ErrorCode.Should().Be(ResultErrorCodes.Unauthorized);
        result.ErrorMessage.Should().Be("Email hoặc mật khẩu không chính xác.");
    }

    [Fact]
    public async Task Login_WhenPasswordIncorrect_ShouldReturnUnauthorized()
    {
        SetupUsers(new List<User> { User.Create("tam@example.com", "Bui Ngoc Tam", "hashed_password") });
        _passwordHasherMock.Setup(h => h.VerifyPassword("WrongPassword", "hashed_password")).Returns(false);

        var result = await Login().Handle(
            new LoginCommand("tam@example.com", "WrongPassword", null), CancellationToken.None);

        result.ErrorCode.Should().Be(ResultErrorCodes.Unauthorized);
        result.ErrorMessage.Should().Be("Email hoặc mật khẩu không chính xác.");
    }

    [Fact]
    public async Task Login_WhenEmailNotConfirmed_ShouldReturnUnauthorized()
    {
        SetupUsers(new List<User> { User.Create("new@example.com", "New", "hashed_password") });
        _passwordHasherMock.Setup(h => h.VerifyPassword("CorrectPassword", "hashed_password")).Returns(true);

        var result = await Login().Handle(
            new LoginCommand("new@example.com", "CorrectPassword", null), CancellationToken.None);

        result.ErrorCode.Should().Be(ResultErrorCodes.Unauthorized);
        result.ErrorMessage.Should().Contain("chưa được kích hoạt");
    }

    [Fact]
    public async Task Login_WhenCredentialsValid_ShouldReturnToken()
    {
        var user = User.Create("tam@example.com", "Bui Ngoc Tam", "hashed_password");
        user.ConfirmEmail();
        SetupUsers(new List<User> { user });
        _passwordHasherMock.Setup(h => h.VerifyPassword("CorrectPassword", "hashed_password")).Returns(true);
        _jwtTokenGeneratorMock.Setup(j => j.GenerateToken(user, It.IsAny<Guid>())).Returns(("valid_jwt_token", DateTime.UtcNow.AddHours(24)));

        var result = await Login().Handle(
            new LoginCommand("tam@example.com", "CorrectPassword", null), CancellationToken.None);

        result.Value!.Token.Should().Be("valid_jwt_token");
        result.Value.User!.Email.Should().Be("tam@example.com");
        _auditLogs.Should().ContainSingle(a => a.Action == "LOGIN" && a.UserId == user.Id && a.UserEmail == "tam@example.com");
    }

    [Fact]
    public async Task Login_WhenTwoFactorEnabled_AndNoRememberToken_ShouldRequireTwoFactor()
    {
        var user = User.Create("2fa@example.com", "2FA User", "hashed_password");
        user.ConfirmEmail();
        user.SetTwoFactorSecret("JBSWY3DPEHPK3PXP");
        user.EnableTwoFactor();
        SetupUsers(new List<User> { user });
        _passwordHasherMock.Setup(h => h.VerifyPassword("CorrectPassword", "hashed_password")).Returns(true);

        var result = await Login().Handle(
            new LoginCommand("2fa@example.com", "CorrectPassword", null), CancellationToken.None);

        result.Value!.Token.Should().BeNull();
        result.Value.RequiresTwoFactor.Should().BeTrue();
        result.Value.TwoFactorEmail.Should().Be("2fa@example.com");
    }

    [Fact]
    public async Task Login_WhenTwoFactorEnabled_AndDeviceRemembered_ShouldIssueTokenDirectly()
    {
        var user = User.Create("user@example.com", "Test User", "hashed_pwd");
        user.ConfirmEmail();
        user.SetTwoFactorSecret("JBSWY3DPEHPK3PXP");
        user.EnableTwoFactor();
        SetupUsers(new List<User> { user });

        const string plainToken = "sample_remember_device_token";
        SetupRememberedDevices(new List<UserRememberedDevice>
        {
            UserRememberedDevice.Create(user.Id, HashToken(plainToken), DateTime.UtcNow.AddDays(30), "My PC")
        });
        _passwordHasherMock.Setup(p => p.VerifyPassword("Password123!", "hashed_pwd")).Returns(true);
        _jwtTokenGeneratorMock.Setup(j => j.GenerateToken(user, It.IsAny<Guid>())).Returns(("trusted-jwt-token", DateTime.UtcNow.AddDays(7)));

        var result = await Login().Handle(
            new LoginCommand("user@example.com", "Password123!", plainToken), CancellationToken.None);

        result.Value!.RequiresTwoFactor.Should().BeFalse();
        result.Value.Token.Should().Be("trusted-jwt-token");
        result.Value.Message.Should().Contain("Thiết bị tin cậy");
    }

    // ══════════════════════════════════════════════════════════════════════════
    // CHANGE PASSWORD
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ChangePassword_WhenValid_ShouldUpdatePasswordHash()
    {
        var user = User.Create("user@example.com", "User", "old_hash");
        user.SetPasswordResetToken(AuthSecrets.ChangePasswordOtp("123456"), DateTime.UtcNow.AddMinutes(15));
        SetupUsers(new List<User> { user });
        _passwordHasherMock.Setup(h => h.VerifyPassword("OldPassword123!", "old_hash")).Returns(true);
        _passwordHasherMock.Setup(h => h.VerifyPassword("NewPassword123!", "old_hash")).Returns(false);
        _passwordHasherMock.Setup(h => h.HashPassword("NewPassword123!")).Returns("new_hash");

        var result = await ChangePassword().Handle(
            new ChangePasswordCommand(user.Id, "OldPassword123!", "NewPassword123!", "123456", null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        user.PasswordHash.Should().Be("new_hash");
        _dbContextMock.Verify(db => db.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ChangePassword_ShouldRevokeOtherSessionsButKeepCurrentOne()
    {
        var user = User.Create("user@example.com", "User", "old_hash");
        user.SetPasswordResetToken(AuthSecrets.ChangePasswordOtp("123456"), DateTime.UtcNow.AddMinutes(15));
        SetupUsers(new List<User> { user });
        _passwordHasherMock.Setup(h => h.VerifyPassword("OldPassword123!", "old_hash")).Returns(true);
        _passwordHasherMock.Setup(h => h.VerifyPassword("NewPassword123!", "old_hash")).Returns(false);
        _passwordHasherMock.Setup(h => h.HashPassword("NewPassword123!")).Returns("new_hash");
        var current = RefreshToken.Create(user.Id, HashToken("current-session"), DateTime.UtcNow.AddDays(1));
        var otherDevice = RefreshToken.Create(user.Id, HashToken("other-device"), DateTime.UtcNow.AddDays(1));
        _refreshTokens.AddRange(new[] { current, otherDevice });

        var result = await ChangePassword().Handle(
            new ChangePasswordCommand(user.Id, "OldPassword123!", "NewPassword123!", "123456", null, "current-session"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        current.IsRevoked.Should().BeFalse("phiên đang đổi mật khẩu phải được giữ lại");
        otherDevice.IsRevoked.Should().BeTrue();
    }

    [Fact]
    public async Task ChangePassword_WithoutCurrentRefreshToken_ShouldRevokeAllSessions()
    {
        var user = User.Create("user@example.com", "User", "old_hash");
        user.SetPasswordResetToken(AuthSecrets.ChangePasswordOtp("123456"), DateTime.UtcNow.AddMinutes(15));
        SetupUsers(new List<User> { user });
        _passwordHasherMock.Setup(h => h.VerifyPassword("OldPassword123!", "old_hash")).Returns(true);
        _passwordHasherMock.Setup(h => h.VerifyPassword("NewPassword123!", "old_hash")).Returns(false);
        _passwordHasherMock.Setup(h => h.HashPassword("NewPassword123!")).Returns("new_hash");
        var session = RefreshToken.Create(user.Id, HashToken("session"), DateTime.UtcNow.AddDays(1));
        _refreshTokens.Add(session);

        await ChangePassword().Handle(
            new ChangePasswordCommand(user.Id, "OldPassword123!", "NewPassword123!", "123456", null), CancellationToken.None);

        session.IsRevoked.Should().BeTrue();
    }

    [Fact]
    public async Task ChangePassword_WhenCurrentPasswordIncorrect_ShouldReturnUnauthorized()
    {
        var user = User.Create("user@example.com", "User", "old_hash");
        SetupUsers(new List<User> { user });
        _passwordHasherMock.Setup(h => h.VerifyPassword("WrongPassword", "old_hash")).Returns(false);

        var result = await ChangePassword().Handle(
            new ChangePasswordCommand(user.Id, "WrongPassword", "NewPassword123!", null, null), CancellationToken.None);

        result.ErrorCode.Should().Be(ResultErrorCodes.Unauthorized);
        result.ErrorMessage.Should().Be("Mật khẩu hiện tại không chính xác.");
    }

    [Fact]
    public async Task ChangePassword_WhenNewPasswordSameAsCurrent_ShouldFailWithBadRequest()
    {
        var user = User.Create("user@example.com", "User", "hash");
        SetupUsers(new List<User> { user });
        _passwordHasherMock.Setup(h => h.VerifyPassword("SamePassword123!", "hash")).Returns(true);

        var result = await ChangePassword().Handle(
            new ChangePasswordCommand(user.Id, "SamePassword123!", "SamePassword123!", null, null), CancellationToken.None);

        result.ErrorCode.Should().BeNull();
        result.ErrorMessage.Should().Be("Mật khẩu mới không được trùng với mật khẩu hiện tại.");
    }

    // ══════════════════════════════════════════════════════════════════════════
    // FORGOT & RESET PASSWORD
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ForgotPassword_WhenUserNotFound_ShouldSucceedSilently()
    {
        SetupUsers(new List<User>());

        var result = await new ForgotPasswordCommandHandler(_dbContextMock.Object, _emailServiceMock.Object)
            .Handle(new ForgotPasswordCommand("nonexistent@example.com"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _emailServiceMock.Verify(e => e.SendPasswordResetEmailAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ForgotPassword_WhenUserExists_ShouldSetResetTokenAndSendEmail()
    {
        var user = User.Create("user@example.com", "User", "hash");
        SetupUsers(new List<User> { user });

        await new ForgotPasswordCommandHandler(_dbContextMock.Object, _emailServiceMock.Object)
            .Handle(new ForgotPasswordCommand("user@example.com"), CancellationToken.None);

        user.PasswordResetToken.Should().NotBeNullOrEmpty();
        user.PasswordResetTokenExpiresAt.Should().BeAfter(DateTime.UtcNow);
        _dbContextMock.Verify(db => db.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _emailServiceMock.Verify(e => e.SendPasswordResetEmailAsync(
            user.Email, It.IsAny<string>(), user.FullName, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ResetPassword_WhenTokenValid_ShouldUpdatePasswordHash()
    {
        var user = User.Create("user@example.com", "User", "old_hash");
        user.SetPasswordResetToken("123456", DateTime.UtcNow.AddMinutes(15));
        SetupUsers(new List<User> { user });
        _passwordHasherMock.Setup(h => h.HashPassword("NewSecret123!")).Returns("new_hashed_pwd");

        var result = await new ResetPasswordCommandHandler(_dbContextMock.Object, _passwordHasherMock.Object)
            .Handle(new ResetPasswordCommand("user@example.com", "123456", "NewSecret123!"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        user.PasswordHash.Should().Be("new_hashed_pwd");
        user.PasswordResetToken.Should().BeNull();
        _dbContextMock.Verify(db => db.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ResetPassword_WhenTokenMismatch_ShouldFail()
    {
        var user = User.Create("user@example.com", "User", "old_hash");
        user.SetPasswordResetToken("123456", DateTime.UtcNow.AddMinutes(15));
        SetupUsers(new List<User> { user });

        var result = await new ResetPasswordCommandHandler(_dbContextMock.Object, _passwordHasherMock.Object)
            .Handle(new ResetPasswordCommand("user@example.com", "999999", "NewSecret123!"), CancellationToken.None);

        result.ErrorMessage.Should().StartWith("Mã xác thực đặt lại mật khẩu không chính xác.");
    }

    [Fact]
    public async Task ResetPassword_WhenTokenExpired_ShouldFail()
    {
        var user = User.Create("user@example.com", "User", "old_hash");
        user.SetPasswordResetToken("123456", DateTime.UtcNow.AddMinutes(-5));
        SetupUsers(new List<User> { user });

        var result = await new ResetPasswordCommandHandler(_dbContextMock.Object, _passwordHasherMock.Object)
            .Handle(new ResetPasswordCommand("user@example.com", "123456", "NewSecret123!"), CancellationToken.None);

        result.ErrorMessage.Should().Contain("đã hết hạn");
    }

    // ══════════════════════════════════════════════════════════════════════════
    // EMAIL VERIFICATION
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task VerifyEmail_WhenValidToken_ShouldConfirmEmail()
    {
        var user = User.Create("user@example.com", "User", "hash");
        user.SetEmailVerificationToken("654321", DateTime.UtcNow.AddHours(24));
        SetupUsers(new List<User> { user });

        var result = await new VerifyEmailCommandHandler(_dbContextMock.Object, _jwtTokenGeneratorMock.Object)
            .Handle(new VerifyEmailCommand("user@example.com", "654321"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        user.IsEmailConfirmed.Should().BeTrue();
        user.EmailVerificationToken.Should().BeNull();
        _dbContextMock.Verify(db => db.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(2)); // xác nhận email + lưu refresh token
    }

    [Fact]
    public async Task VerifyEmail_WhenAlreadyConfirmed_ShouldNotSave()
    {
        var user = User.Create("user@example.com", "User", "hash");
        user.ConfirmEmail();
        SetupUsers(new List<User> { user });

        var result = await new VerifyEmailCommandHandler(_dbContextMock.Object, _jwtTokenGeneratorMock.Object)
            .Handle(new VerifyEmailCommand("user@example.com", "654321"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _dbContextMock.Verify(db => db.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task VerifyEmail_WhenAlreadyConfirmed_ShouldNotIssueToken()
    {
        var user = User.Create("user@example.com", "User", "hash");
        user.ConfirmEmail();
        SetupUsers(new List<User> { user });

        var result = await new VerifyEmailCommandHandler(_dbContextMock.Object, _jwtTokenGeneratorMock.Object)
            .Handle(new VerifyEmailCommand("user@example.com", "bat-ky"), CancellationToken.None);

        result.Value!.Token.Should().BeNull();
        result.Value.RefreshToken.Should().BeNull();
    }

    [Fact]
    public async Task ResetPassword_After5WrongOtps_ShouldInvalidateOtpEvenIfNextGuessIsCorrect()
    {
        var user = User.Create("user@example.com", "User", "old_hash");
        user.SetPasswordResetToken("123456", DateTime.UtcNow.AddMinutes(15));
        SetupUsers(new List<User> { user });
        var handler = new ResetPasswordCommandHandler(_dbContextMock.Object, _passwordHasherMock.Object);

        for (var i = 0; i < User.MaxOtpFailedAttempts; i++)
            await handler.Handle(new ResetPasswordCommand("user@example.com", "000000", "NewSecret123!"), CancellationToken.None);

        var result = await handler.Handle(
            new ResetPasswordCommand("user@example.com", "123456", "NewSecret123!"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        user.PasswordResetToken.Should().BeNull();
    }

    [Fact]
    public async Task Login_WhenValid_ShouldIssueRefreshTokenStoredAsHash()
    {
        var user = User.Create("user@example.com", "User", "hash");
        user.ConfirmEmail();
        SetupUsers(new List<User> { user });
        _passwordHasherMock.Setup(p => p.VerifyPassword("Password123!", "hash")).Returns(true);
        _jwtTokenGeneratorMock.Setup(j => j.GenerateToken(user, It.IsAny<Guid>())).Returns(("jwt", DateTime.UtcNow.AddMinutes(30)));

        var result = await Login().Handle(new LoginCommand("user@example.com", "Password123!", null), CancellationToken.None);

        result.Value!.RefreshToken.Should().NotBeNullOrEmpty();
        _refreshTokens.Should().ContainSingle().Which.TokenHash.Should().Be(HashToken(result.Value.RefreshToken!));
    }

    [Fact]
    public async Task RefreshToken_WhenValid_ShouldRotate()
    {
        var user = User.Create("user@example.com", "User", "hash");
        SetupUsers(new List<User> { user });
        var old = RefreshToken.Create(user.Id, HashToken("old-token"), DateTime.UtcNow.AddDays(1));
        _refreshTokens.Add(old);
        _jwtTokenGeneratorMock.Setup(j => j.GenerateToken(It.IsAny<User>(), It.IsAny<Guid>())).Returns(("jwt", DateTime.UtcNow.AddMinutes(30)));
        SetupRefreshTokenUser(old, user);

        var result = await new RefreshTokenCommandHandler(_dbContextMock.Object, _jwtTokenGeneratorMock.Object)
            .Handle(new RefreshTokenCommand("old-token"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        old.IsRevoked.Should().BeTrue();
        result.Value!.RefreshToken.Should().NotBe("old-token");
        _refreshTokens.Should().HaveCount(2);
    }

    [Fact]
    public async Task RefreshToken_WhenRevokedTokenReused_ShouldRevokeAllSessions()
    {
        var user = User.Create("user@example.com", "User", "hash");
        var stolen = RefreshToken.Create(user.Id, HashToken("stolen"), DateTime.UtcNow.AddDays(1));
        stolen.Revoke();
        var other = RefreshToken.Create(user.Id, HashToken("other-session"), DateTime.UtcNow.AddDays(1));
        _refreshTokens.AddRange(new[] { stolen, other });
        SetupRefreshTokenUser(stolen, user);

        var result = await new RefreshTokenCommandHandler(_dbContextMock.Object, _jwtTokenGeneratorMock.Object)
            .Handle(new RefreshTokenCommand("stolen"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ResultErrorCodes.Unauthorized);
        other.IsRevoked.Should().BeTrue();
        _auditLogs.Should().ContainSingle(a => a.Action == "SESSIONS_REVOKED" && a.UserId == user.Id);
    }

    [Fact]
    public async Task RefreshToken_WhenUserDeactivated_ShouldFail()
    {
        var user = User.Create("user@example.com", "User", "hash");
        user.Deactivate();
        var token = RefreshToken.Create(user.Id, HashToken("t"), DateTime.UtcNow.AddDays(1));
        _refreshTokens.Add(token);
        SetupRefreshTokenUser(token, user);

        var result = await new RefreshTokenCommandHandler(_dbContextMock.Object, _jwtTokenGeneratorMock.Object)
            .Handle(new RefreshTokenCommand("t"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
    }

    // Include(t => t.User) không chạy trên mock → gán sẵn navigation qua reflection
    private static void SetupRefreshTokenUser(RefreshToken token, User user)
        => typeof(RefreshToken).GetProperty(nameof(RefreshToken.User))!.SetValue(token, user);

    [Fact]
    public async Task ResendVerification_WhenAlreadyConfirmed_ShouldFail()
    {
        var user = User.Create("user@example.com", "User", "hash");
        user.ConfirmEmail();
        SetupUsers(new List<User> { user });

        var result = await new ResendVerificationEmailCommandHandler(_dbContextMock.Object, _emailServiceMock.Object)
            .Handle(new ResendVerificationEmailCommand("user@example.com"), CancellationToken.None);

        result.ErrorMessage.Should().Be("Tài khoản email này đã được xác thực trước đó.");
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 2FA
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task EnableTwoFactor_WhenValidUser_ShouldGenerateSecretAndUri()
    {
        var user = User.Create("user@example.com", "User", "hash");
        SetupUsers(new List<User> { user });

        var result = await new EnableTwoFactorCommandHandler(_dbContextMock.Object, _totpService)
            .Handle(new EnableTwoFactorCommand(user.Id), CancellationToken.None);

        var response = result.Value!;
        response.SharedKey.Should().NotBeNullOrEmpty();
        response.AuthenticatorUri.Should().Contain("otpauth://totp/PanelForge");
        response.FormattedKey.Should().NotBeNullOrEmpty();
        user.TwoFactorSecret.Should().Be(response.SharedKey);
        _dbContextMock.Verify(db => db.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    private static (User user, string code) CreateTwoFactorUser()
    {
        var user = User.Create("user@example.com", "Test User", "hashed_pwd");
        user.ConfirmEmail();
        var secretBytes = KeyGeneration.GenerateRandomKey(20);
        user.SetTwoFactorSecret(Base32Encoding.ToString(secretBytes));
        user.EnableTwoFactor();
        return (user, new Totp(secretBytes).ComputeTotp());
    }

    [Fact]
    public async Task VerifyTwoFactor_WhenRememberDeviceIsTrue_ShouldSaveDeviceAndReturnPlainToken()
    {
        var (user, code) = CreateTwoFactorUser();
        SetupUsers(new List<User> { user });
        _jwtTokenGeneratorMock.Setup(j => j.GenerateToken(user, It.IsAny<Guid>())).Returns(("jwt-2fa-token", DateTime.UtcNow.AddDays(7)));

        var result = await VerifyTwoFactor().Handle(
            new VerifyTwoFactorCommand(null, code, user.Email, true, "Office Desktop", ChallengeEmail: user.Email), CancellationToken.None);

        var response = result.Value!;
        response.Token.Should().Be("jwt-2fa-token");
        response.RememberDeviceToken.Should().NotBeNullOrEmpty();
        response.Message.Should().Contain("ghi nhớ trong 30 ngày");

        _rememberedDevices.Should().ContainSingle();
        var savedDevice = _rememberedDevices[0];
        savedDevice.UserId.Should().Be(user.Id);
        savedDevice.DeviceName.Should().Be("Office Desktop");
        savedDevice.TokenHash.Should().Be(HashToken(response.RememberDeviceToken!));
        savedDevice.ExpiresAt.Should().BeAfter(DateTime.UtcNow.AddDays(29));
    }

    [Fact]
    public async Task VerifyTwoFactor_WhenRememberDeviceIsFalse_ShouldNotSaveDevice()
    {
        var (user, code) = CreateTwoFactorUser();
        SetupUsers(new List<User> { user });
        _jwtTokenGeneratorMock.Setup(j => j.GenerateToken(user, It.IsAny<Guid>())).Returns(("jwt-2fa-token", DateTime.UtcNow.AddDays(7)));

        var result = await VerifyTwoFactor().Handle(
            new VerifyTwoFactorCommand(null, code, user.Email, false, null, ChallengeEmail: user.Email), CancellationToken.None);

        result.Value!.Token.Should().Be("jwt-2fa-token");
        result.Value.RememberDeviceToken.Should().BeNull();
        _rememberedDevices.Should().BeEmpty();
    }

    [Fact]
    public async Task VerifyTwoFactor_WhenCodeWrong_ShouldReturnUnauthorized()
    {
        var (user, _) = CreateTwoFactorUser();
        SetupUsers(new List<User> { user });

        var result = await VerifyTwoFactor().Handle(
            new VerifyTwoFactorCommand(null, "000000", user.Email, false, null, ChallengeEmail: user.Email), CancellationToken.None);

        result.ErrorCode.Should().Be(ResultErrorCodes.Unauthorized);
    }

    [Fact]
    public async Task ForgetDevice_WhenTokenProvided_ShouldRemoveDevice()
    {
        var userId = Guid.NewGuid();
        const string plainToken = "token-to-revoke-12345";
        _rememberedDevices.Add(UserRememberedDevice.Create(userId, HashToken(plainToken), DateTime.UtcNow.AddDays(30), "Old Device"));
        _dbContextMock.Setup(db => db.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        await new ForgetDeviceCommandHandler(_dbContextMock.Object)
            .Handle(new ForgetDeviceCommand(plainToken, userId), CancellationToken.None);

        _rememberedDevices.Should().BeEmpty();
        _dbContextMock.Verify(db => db.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task VerifyTwoFactor_WithoutPasswordStepChallenge_ShouldBeRejected_EvenWithCorrectCode()
    {
        var (user, code) = CreateTwoFactorUser();
        SetupUsers(new List<User> { user });

        var result = await VerifyTwoFactor().Handle(
            new VerifyTwoFactorCommand(null, code, user.Email, false, null), CancellationToken.None);

        result.ErrorCode.Should().Be(ResultErrorCodes.Unauthorized);
        _refreshTokens.Should().BeEmpty();
    }

    [Fact]
    public async Task VerifyTwoFactor_WhenBodyEmailDiffersFromChallenge_ShouldBeRejected()
    {
        var (user, code) = CreateTwoFactorUser();
        SetupUsers(new List<User> { user });

        var result = await VerifyTwoFactor().Handle(
            new VerifyTwoFactorCommand(null, code, "other@example.com", false, null, ChallengeEmail: user.Email),
            CancellationToken.None);

        result.ErrorCode.Should().Be(ResultErrorCodes.Unauthorized);
        _refreshTokens.Should().BeEmpty();
    }

    [Fact]
    public async Task VerifyTwoFactor_WithChallenge_CompletesLogin_EvenWhenAnotherUserIsSignedIn()
    {
        var (user, code) = CreateTwoFactorUser();
        SetupUsers(new List<User> { user });
        _jwtTokenGeneratorMock.Setup(j => j.GenerateToken(user, It.IsAny<Guid>())).Returns(("jwt-2fa-token", DateTime.UtcNow.AddDays(7)));

        var result = await VerifyTwoFactor().Handle(
            new VerifyTwoFactorCommand(Guid.NewGuid(), code, null, false, null, ChallengeEmail: user.Email),
            CancellationToken.None);

        result.Value!.Token.Should().Be("jwt-2fa-token");
    }

    [Fact]
    public async Task EnableTwoFactor_WhenAlreadyEnabled_ShouldFail_AndKeepCurrentSecret()
    {
        var (user, _) = CreateTwoFactorUser();
        var currentSecret = user.TwoFactorSecret;
        SetupUsers(new List<User> { user });

        var result = await new EnableTwoFactorCommandHandler(_dbContextMock.Object, _totpService)
            .Handle(new EnableTwoFactorCommand(user.Id), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        user.TwoFactorSecret.Should().Be(currentSecret);
    }

    private DisableTwoFactorCommandHandler DisableTwoFactor()
        => new(_dbContextMock.Object, _passwordHasherMock.Object, _totpService);

    [Fact]
    public async Task DisableTwoFactor_WithPasswordAndCode_ShouldDisable_AndForgetTrustedDevices()
    {
        var (user, code) = CreateTwoFactorUser();
        SetupUsers(new List<User> { user });
        _passwordHasherMock.Setup(h => h.VerifyPassword("Password123!", "hashed_pwd")).Returns(true);
        _rememberedDevices.Add(UserRememberedDevice.Create(user.Id, "hash", DateTime.UtcNow.AddDays(30), "Laptop"));

        var result = await DisableTwoFactor().Handle(
            new DisableTwoFactorCommand(user.Id, code, "Password123!"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        user.TwoFactorEnabled.Should().BeFalse();
        user.TwoFactorSecret.Should().BeNull();
        _rememberedDevices.Should().BeEmpty();
    }

    [Theory]
    [InlineData("WrongPassword", true)]
    [InlineData("Password123!", false)]
    public async Task DisableTwoFactor_WithWrongPasswordOrCode_ShouldBeUnauthorized_AndStayEnabled(string password, bool useValidCode)
    {
        var (user, code) = CreateTwoFactorUser();
        SetupUsers(new List<User> { user });
        _passwordHasherMock.Setup(h => h.VerifyPassword("Password123!", "hashed_pwd")).Returns(true);

        var result = await DisableTwoFactor().Handle(
            new DisableTwoFactorCommand(user.Id, useValidCode ? code : "000000", password), CancellationToken.None);

        result.ErrorCode.Should().Be(ResultErrorCodes.Unauthorized);
        user.TwoFactorEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task ResetPassword_ShouldForgetTrustedDevices()
    {
        var user = User.Create("user@example.com", "User", "old_hash");
        user.SetPasswordResetToken("123456", DateTime.UtcNow.AddMinutes(15));
        SetupUsers(new List<User> { user });
        _passwordHasherMock.Setup(h => h.HashPassword("NewSecret123!")).Returns("new_hashed_pwd");
        _rememberedDevices.Add(UserRememberedDevice.Create(user.Id, "hash", DateTime.UtcNow.AddDays(30), "Laptop"));

        var result = await new ResetPasswordCommandHandler(_dbContextMock.Object, _passwordHasherMock.Object)
            .Handle(new ResetPasswordCommand("user@example.com", "123456", "NewSecret123!"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _rememberedDevices.Should().BeEmpty();
    }

    [Fact]
    public async Task ResetPassword_ShouldRejectOtpIssuedForChangePassword()
    {
        var user = User.Create("user@example.com", "User", "old_hash");
        user.SetPasswordResetToken(AuthSecrets.ChangePasswordOtp("123456"), DateTime.UtcNow.AddMinutes(15));
        SetupUsers(new List<User> { user });

        var result = await new ResetPasswordCommandHandler(_dbContextMock.Object, _passwordHasherMock.Object)
            .Handle(new ResetPasswordCommand("user@example.com", "123456", "NewSecret123!"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        user.PasswordHash.Should().Be("old_hash");
    }

    [Fact]
    public async Task ChangePassword_ShouldRejectOtpIssuedForPasswordReset()
    {
        var user = User.Create("user@example.com", "User", "old_hash");
        user.SetPasswordResetToken("123456", DateTime.UtcNow.AddMinutes(15));
        SetupUsers(new List<User> { user });
        _passwordHasherMock.Setup(h => h.VerifyPassword("OldPassword123!", "old_hash")).Returns(true);

        var result = await new ChangePasswordCommandHandler(_dbContextMock.Object, _passwordHasherMock.Object, _totpService).Handle(
            new ChangePasswordCommand(user.Id, "OldPassword123!", "NewPassword123!", "123456", null), CancellationToken.None);

        result.ErrorCode.Should().Be(ResultErrorCodes.Unauthorized);
        user.PasswordHash.Should().Be("old_hash");
    }

    // ══════════════════════════════════════════════════════════════════════════
    // EXTERNAL LOGIN (Firebase qua IExternalIdentityVerifier)
    // ══════════════════════════════════════════════════════════════════════════

    private ExternalLoginCommandHandler ExternalLogin(ExternalIdentityResult identity)
    {
        var verifier = new Mock<IExternalIdentityVerifier>();
        verifier.Setup(v => v.VerifyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(identity);
        return new ExternalLoginCommandHandler(_dbContextMock.Object, verifier.Object, _jwtTokenGeneratorMock.Object);
    }

    [Fact]
    public async Task ExternalLogin_WhenTokenInvalid_ShouldReturnUnauthorized()
    {
        SetupUsers(new List<User>());

        var result = await ExternalLogin(new ExternalIdentityResult(ExternalIdentityStatus.InvalidToken, ErrorMessage: "bad"))
            .Handle(new ExternalLoginCommand("x"), CancellationToken.None);

        result.ErrorCode.Should().Be(ResultErrorCodes.Unauthorized);
    }

    [Fact]
    public async Task ExternalLogin_WhenProviderNotConfigured_ShouldReturnBadRequest()
    {
        SetupUsers(new List<User>());

        var result = await ExternalLogin(new ExternalIdentityResult(ExternalIdentityStatus.NotConfigured, ErrorMessage: "no firebase"))
            .Handle(new ExternalLoginCommand("x"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().BeNull();
    }

    [Fact]
    public async Task ExternalLogin_WhenNewUser_ShouldCreateConfirmedAccountAndSignIn()
    {
        var users = new List<User>();
        SetupUsers(users);
        _jwtTokenGeneratorMock.Setup(j => j.GenerateToken(It.IsAny<User>(), It.IsAny<Guid>())).Returns(("jwt", DateTime.UtcNow.AddDays(1)));

        var result = await ExternalLogin(new ExternalIdentityResult(
                ExternalIdentityStatus.Valid, Uid: "fb-uid", Email: " New@Gmail.com ", Name: null))
            .Handle(new ExternalLoginCommand("x"), CancellationToken.None);

        result.Value!.Token.Should().Be("jwt");
        var created = users.Should().ContainSingle().Subject;
        created.Email.Should().Be("new@gmail.com");
        created.FullName.Should().Be("new");
        created.FirebaseUid.Should().Be("fb-uid");
        created.IsEmailConfirmed.Should().BeTrue();
    }
}
