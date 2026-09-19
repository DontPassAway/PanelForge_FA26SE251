using FluentAssertions;
using Moq;
using PanelForge.Application.DTOs.Auth;
using PanelForge.Application.Interfaces;
using PanelForge.Application.Interfaces.Authentication;
using PanelForge.Application.Interfaces.Persistence;
using PanelForge.Domain.Entities.Auth;
using PanelForge.Infrastructure.Services;
using PanelForge.UnitTests.Common;
using Xunit;

namespace PanelForge.UnitTests.Infrastructure.Authentication;

public class AuthServiceTests
{
    private readonly Mock<IPasswordHasher> _passwordHasherMock;
    private readonly Mock<IJwtTokenGenerator> _jwtTokenGeneratorMock;
    private readonly Mock<IPanelForgeDbContext> _dbContextMock;
    private readonly Mock<IEmailService> _emailServiceMock;
    private readonly AuthService _service;

    public AuthServiceTests()
    {
        _passwordHasherMock = new Mock<IPasswordHasher>();
        _jwtTokenGeneratorMock = new Mock<IJwtTokenGenerator>();
        _dbContextMock = new Mock<IPanelForgeDbContext>();
        _emailServiceMock = new Mock<IEmailService>();

        _service = new AuthService(
            _dbContextMock.Object,
            _passwordHasherMock.Object,
            _jwtTokenGeneratorMock.Object,
            _emailServiceMock.Object);
    }

    private void SetupUsers(List<User> users)
    {
        var dbSet = DbSetMockHelper.CreateDbSetMock(users);
        _dbContextMock.Setup(db => db.Users).Returns(dbSet);
        _dbContextMock.Setup(db => db.SaveChangesAsync(It.IsAny<CancellationToken>()))
                      .ReturnsAsync(1);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // REGISTER TESTS
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task RegisterAsync_WhenEmailAlreadyExists_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var existingUser = User.Create("test@example.com", "Existing User", "hashed_pwd");
        SetupUsers(new List<User> { existingUser });

        var request = new RegisterRequest("test@example.com", "Password123!", "New User");

        // Act
        Func<Task> act = async () => await _service.RegisterAsync(request);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Email đã được đăng ký trong hệ thống.");
    }

    [Fact]
    public async Task RegisterAsync_WhenValidRequest_ShouldCreateUserAndReturnToken()
    {
        // Arrange
        var usersList = new List<User>();
        SetupUsers(usersList);

        _passwordHasherMock.Setup(h => h.HashPassword("Password123!"))
                           .Returns("hashed_secure_password");

        _emailServiceMock.Setup(e => e.SendEmailVerificationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                         .Returns(Task.CompletedTask);

        var request = new RegisterRequest("tam@example.com", "Password123!", "Bui Ngoc Tam", "0123456789");

        // Act
        var result = await _service.RegisterAsync(request);

        // Assert
        // Registration no longer returns a JWT token; the user must verify their email first.
        result.Should().NotBeNull();
        result.Token.Should().BeNull();
        result.ExpiresAt.Should().BeNull();
        result.User!.Email.Should().Be("tam@example.com");
        result.User.FullName.Should().Be("Bui Ngoc Tam");
        result.Message.Should().Contain("xác thực");

        usersList.Should().ContainSingle(u => u.Email == "tam@example.com");
        _dbContextMock.Verify(db => db.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _emailServiceMock.Verify(e => e.SendEmailVerificationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // LOGIN TESTS
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task LoginAsync_WhenUserNotFound_ShouldThrowUnauthorizedAccessException()
    {
        // Arrange
        SetupUsers(new List<User>());
        var request = new LoginRequest("notfound@example.com", "Password123!");

        // Act
        Func<Task> act = async () => await _service.LoginAsync(request);

        // Assert
        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("Email hoặc mật khẩu không chính xác.");
    }

    [Fact]
    public async Task LoginAsync_WhenUserInactive_ShouldThrowUnauthorizedAccessException()
    {
        // Arrange
        var user = User.Create("inactive@example.com", "Inactive User", "hashed_pwd");
        user.Deactivate();
        SetupUsers(new List<User> { user });

        var request = new LoginRequest("inactive@example.com", "Password123!");

        // Act
        Func<Task> act = async () => await _service.LoginAsync(request);

        // Assert
        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("Email hoặc mật khẩu không chính xác.");
    }

    [Fact]
    public async Task LoginAsync_WhenPasswordIsIncorrect_ShouldThrowUnauthorizedAccessException()
    {
        // Arrange
        var user = User.Create("tam@example.com", "Bui Ngoc Tam", "hashed_password");
        SetupUsers(new List<User> { user });

        _passwordHasherMock.Setup(h => h.VerifyPassword("WrongPassword", "hashed_password"))
                           .Returns(false);

        var request = new LoginRequest("tam@example.com", "WrongPassword");

        // Act
        Func<Task> act = async () => await _service.LoginAsync(request);

        // Assert
        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("Email hoặc mật khẩu không chính xác.");
    }

    [Fact]
    public async Task LoginAsync_WhenCredentialsAreValid_ShouldReturnToken()
    {
        // Arrange
        var user = User.Create("tam@example.com", "Bui Ngoc Tam", "hashed_password");
        user.ConfirmEmail(); // Email must be confirmed before login is allowed
        SetupUsers(new List<User> { user });

        _passwordHasherMock.Setup(h => h.VerifyPassword("CorrectPassword", "hashed_password"))
                           .Returns(true);

        var fakeExpiry = DateTime.UtcNow.AddHours(24);
        _jwtTokenGeneratorMock.Setup(j => j.GenerateToken(user))
                              .Returns(("valid_jwt_token", fakeExpiry));

        var request = new LoginRequest("tam@example.com", "CorrectPassword");

        // Act
        var result = await _service.LoginAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.Token.Should().Be("valid_jwt_token");
        result.User!.Email.Should().Be("tam@example.com");
    }

    [Fact]
    public async Task LoginAsync_WhenTwoFactorEnabled_ShouldRequireTwoFactor()
    {
        // Arrange
        var user = User.Create("2fa@example.com", "2FA User", "hashed_password");
        user.ConfirmEmail(); // Email must be confirmed before login is allowed
        user.SetTwoFactorSecret("JBSWY3DPEHPK3PXP");
        user.EnableTwoFactor();
        SetupUsers(new List<User> { user });

        _passwordHasherMock.Setup(h => h.VerifyPassword("CorrectPassword", "hashed_password"))
                           .Returns(true);

        var request = new LoginRequest("2fa@example.com", "CorrectPassword");

        // Act
        var result = await _service.LoginAsync(request);

        // Assert
        result.RequiresTwoFactor.Should().BeTrue();
        result.TwoFactorEmail.Should().Be("2fa@example.com");
        result.Token.Should().BeNull();
    }

    // ══════════════════════════════════════════════════════════════════════════
    // CHANGE PASSWORD TESTS
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ChangePasswordAsync_WhenValid_ShouldUpdatePasswordHash()
    {
        // Arrange
        var user = User.Create("user@example.com", "User", "old_hash");
        SetupUsers(new List<User> { user });

        _passwordHasherMock.Setup(h => h.VerifyPassword("OldPassword123!", "old_hash"))
                           .Returns(true);
        _passwordHasherMock.Setup(h => h.VerifyPassword("NewPassword123!", "old_hash"))
                           .Returns(false);
        _passwordHasherMock.Setup(h => h.HashPassword("NewPassword123!"))
                           .Returns("new_hash");

        var request = new ChangePasswordRequest
        {
            CurrentPassword = "OldPassword123!",
            NewPassword = "NewPassword123!",
            ConfirmPassword = "NewPassword123!"
        };

        // Act
        await _service.ChangePasswordAsync(user.Id, request);

        // Assert
        user.PasswordHash.Should().Be("new_hash");
        _dbContextMock.Verify(db => db.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ChangePasswordAsync_WhenCurrentPasswordIncorrect_ShouldThrowUnauthorizedAccessException()
    {
        // Arrange
        var user = User.Create("user@example.com", "User", "old_hash");
        SetupUsers(new List<User> { user });

        _passwordHasherMock.Setup(h => h.VerifyPassword("WrongPassword", "old_hash"))
                           .Returns(false);

        var request = new ChangePasswordRequest
        {
            CurrentPassword = "WrongPassword",
            NewPassword = "NewPassword123!",
            ConfirmPassword = "NewPassword123!"
        };

        // Act
        Func<Task> act = async () => await _service.ChangePasswordAsync(user.Id, request);

        // Assert
        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("Mật khẩu hiện tại không chính xác.");
    }

    [Fact]
    public async Task ChangePasswordAsync_WhenNewPasswordSameAsCurrent_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var user = User.Create("user@example.com", "User", "hash");
        SetupUsers(new List<User> { user });

        _passwordHasherMock.Setup(h => h.VerifyPassword("SamePassword123!", "hash"))
                           .Returns(true);

        var request = new ChangePasswordRequest
        {
            CurrentPassword = "SamePassword123!",
            NewPassword = "SamePassword123!",
            ConfirmPassword = "SamePassword123!"
        };

        // Act
        Func<Task> act = async () => await _service.ChangePasswordAsync(user.Id, request);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Mật khẩu mới không được trùng với mật khẩu hiện tại.");
    }

    // ══════════════════════════════════════════════════════════════════════════
    // FORGOT & RESET PASSWORD TESTS
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ForgotPasswordAsync_WhenUserNotFound_ShouldReturnSilentlyToPreventEnumeration()
    {
        // Arrange
        SetupUsers(new List<User>());
        var request = new ForgotPasswordRequest("nonexistent@example.com");

        // Act
        await _service.ForgotPasswordAsync(request);

        // Assert
        _emailServiceMock.Verify(e => e.SendPasswordResetEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ForgotPasswordAsync_WhenUserExists_ShouldSetResetTokenAndSendEmail()
    {
        // Arrange
        var user = User.Create("user@example.com", "User", "hash");
        SetupUsers(new List<User> { user });

        _emailServiceMock.Setup(e => e.SendPasswordResetEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                         .Returns(Task.CompletedTask);

        var request = new ForgotPasswordRequest("user@example.com");

        // Act
        await _service.ForgotPasswordAsync(request);

        // Assert
        user.PasswordResetToken.Should().NotBeNullOrEmpty();
        user.PasswordResetTokenExpiresAt.Should().BeAfter(DateTime.UtcNow);
        _dbContextMock.Verify(db => db.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _emailServiceMock.Verify(e => e.SendPasswordResetEmailAsync(user.Email, It.IsAny<string>(), user.FullName, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ResetPasswordAsync_WhenTokenValid_ShouldUpdatePasswordHash()
    {
        // Arrange
        var user = User.Create("user@example.com", "User", "old_hash");
        user.SetPasswordResetToken("123456", DateTime.UtcNow.AddMinutes(15));
        SetupUsers(new List<User> { user });

        _passwordHasherMock.Setup(h => h.HashPassword("NewSecret123!"))
                           .Returns("new_hashed_pwd");

        var request = new ResetPasswordRequest
        {
            Email = "user@example.com",
            Token = "123456",
            NewPassword = "NewSecret123!",
            ConfirmPassword = "NewSecret123!"
        };

        // Act
        await _service.ResetPasswordAsync(request);

        // Assert
        user.PasswordHash.Should().Be("new_hashed_pwd");
        user.PasswordResetToken.Should().BeNull();
        _dbContextMock.Verify(db => db.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ResetPasswordAsync_WhenTokenMismatch_ShouldThrowArgumentException()
    {
        // Arrange
        var user = User.Create("user@example.com", "User", "old_hash");
        user.SetPasswordResetToken("123456", DateTime.UtcNow.AddMinutes(15));
        SetupUsers(new List<User> { user });

        var request = new ResetPasswordRequest
        {
            Email = "user@example.com",
            Token = "999999",
            NewPassword = "NewSecret123!",
            ConfirmPassword = "NewSecret123!"
        };

        // Act
        Func<Task> act = async () => await _service.ResetPasswordAsync(request);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("Mã xác thực đặt lại mật khẩu không chính xác.");
    }

    [Fact]
    public async Task ResetPasswordAsync_WhenTokenExpired_ShouldThrowArgumentException()
    {
        // Arrange
        var user = User.Create("user@example.com", "User", "old_hash");
        user.SetPasswordResetToken("123456", DateTime.UtcNow.AddMinutes(-5)); // Expired
        SetupUsers(new List<User> { user });

        var request = new ResetPasswordRequest
        {
            Email = "user@example.com",
            Token = "123456",
            NewPassword = "NewSecret123!",
            ConfirmPassword = "NewSecret123!"
        };

        // Act
        Func<Task> act = async () => await _service.ResetPasswordAsync(request);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*đã hết hạn*");
    }

    // ══════════════════════════════════════════════════════════════════════════
    // EMAIL VERIFICATION TESTS
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task VerifyEmailAsync_WhenValidToken_ShouldConfirmEmail()
    {
        // Arrange
        var user = User.Create("user@example.com", "User", "hash");
        user.SetEmailVerificationToken("654321", DateTime.UtcNow.AddHours(24));
        SetupUsers(new List<User> { user });

        var request = new VerifyEmailRequest("user@example.com", "654321");

        // Act
        await _service.VerifyEmailAsync(request);

        // Assert
        user.IsEmailConfirmed.Should().BeTrue();
        user.EmailVerificationToken.Should().BeNull();
        _dbContextMock.Verify(db => db.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task VerifyEmailAsync_WhenAlreadyConfirmed_ShouldReturnSilently()
    {
        // Arrange
        var user = User.Create("user@example.com", "User", "hash");
        user.ConfirmEmail();
        SetupUsers(new List<User> { user });

        var request = new VerifyEmailRequest("user@example.com", "654321");

        // Act
        await _service.VerifyEmailAsync(request);

        // Assert
        _dbContextMock.Verify(db => db.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ResendVerificationEmailAsync_WhenAlreadyConfirmed_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var user = User.Create("user@example.com", "User", "hash");
        user.ConfirmEmail();
        SetupUsers(new List<User> { user });

        var request = new ResendVerificationRequest("user@example.com");

        // Act
        Func<Task> act = async () => await _service.ResendVerificationEmailAsync(request);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Tài khoản email này đã được xác thực trước đó.");
    }

    [Fact]
    public async Task EnableTwoFactorAsync_WhenValidUser_ShouldGenerateSecretAndUri()
    {
        // Arrange
        var user = User.Create("user@example.com", "User", "hash");
        SetupUsers(new List<User> { user });

        // Act
        var result = await _service.EnableTwoFactorAsync(user.Id);

        // Assert
        result.Should().NotBeNull();
        result.SharedKey.Should().NotBeNullOrEmpty();
        result.AuthenticatorUri.Should().Contain("otpauth://totp/PanelForge");
        result.FormattedKey.Should().NotBeNullOrEmpty();
        user.TwoFactorSecret.Should().Be(result.SharedKey);
        _dbContextMock.Verify(db => db.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
