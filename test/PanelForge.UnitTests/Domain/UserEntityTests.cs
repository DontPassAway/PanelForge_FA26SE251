using FluentAssertions;
using PanelForge.Domain.Entities.Auth;
using Xunit;

namespace PanelForge.UnitTests.Domain;

public class UserEntityTests
{
    [Fact]
    public void Create_ValidParameters_ShouldInstantiateUserWithNormalizedEmail()
    {
        // Act
        var user = User.Create(
            email: "  TestUser@Example.COM  ",
            fullName: "  Nguyen Van A  ",
            passwordHash: "hashed_pass",
            phoneNumber: "0901234567");

        // Assert
        user.Email.Should().Be("testuser@example.com");
        user.FullName.Should().Be("Nguyen Van A");
        user.PasswordHash.Should().Be("hashed_pass");
        user.PhoneNumber.Should().Be("0901234567");
        user.IsActive.Should().BeTrue();
        user.IsEmailConfirmed.Should().BeFalse();
        user.TwoFactorEnabled.Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_EmptyEmailOrName_ShouldThrowArgumentException(string invalidInput)
    {
        // Act & Assert
        Action actEmail = () => User.Create(invalidInput, "Valid Name");
        actEmail.Should().Throw<ArgumentException>();

        Action actName = () => User.Create("valid@email.com", invalidInput);
        actName.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void UpdateProfile_ShouldUpdateNamePhoneAndAvatar()
    {
        // Arrange
        var user = User.Create("user@example.com", "Old Name");

        // Act
        user.UpdateProfile("New Name", "0988776655", "https://avatar.url/pic.jpg");

        // Assert
        user.FullName.Should().Be("New Name");
        user.PhoneNumber.Should().Be("0988776655");
        user.AvatarUrl.Should().Be("https://avatar.url/pic.jpg");
    }

    [Fact]
    public void ConfirmEmail_ShouldSetConfirmedTrue_AndClearToken()
    {
        // Arrange
        var user = User.Create("user@example.com", "Name");
        user.SetEmailVerificationToken("123456", DateTime.UtcNow.AddHours(2));

        // Act
        user.ConfirmEmail();

        // Assert
        user.IsEmailConfirmed.Should().BeTrue();
        user.EmailVerificationToken.Should().BeNull();
        user.EmailVerificationTokenExpiresAt.Should().BeNull();
    }

    [Fact]
    public void ResetPassword_ShouldUpdatePasswordHash_AndClearResetToken()
    {
        // Arrange
        var user = User.Create("user@example.com", "Name", "old_hash");
        user.SetPasswordResetToken("987654", DateTime.UtcNow.AddMinutes(15));

        // Act
        user.ResetPassword("new_hash");

        // Assert
        user.PasswordHash.Should().Be("new_hash");
        user.PasswordResetToken.Should().BeNull();
        user.PasswordResetTokenExpiresAt.Should().BeNull();
    }

    [Fact]
    public void EnableTwoFactor_WithoutSettingSecret_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var user = User.Create("user@example.com", "Name");

        // Act
        Action act = () => user.EnableTwoFactor();

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*secret key*");
    }

    [Fact]
    public void EnableTwoFactor_WithSecret_ShouldEnableSuccessfully()
    {
        // Arrange
        var user = User.Create("user@example.com", "Name");
        user.SetTwoFactorSecret("BASE32SECRETKEY");

        // Act
        user.EnableTwoFactor();

        // Assert
        user.TwoFactorEnabled.Should().BeTrue();
        user.TwoFactorSecret.Should().Be("BASE32SECRETKEY");

        // Act Disable
        user.DisableTwoFactor();
        user.TwoFactorEnabled.Should().BeFalse();
        user.TwoFactorSecret.Should().BeNull();
    }

    [Fact]
    public void ActivateAndDeactivate_ShouldToggleIsActive()
    {
        // Arrange
        var user = User.Create("user@example.com", "Name");
        user.IsActive.Should().BeTrue();

        // Act
        user.Deactivate();
        user.IsActive.Should().BeFalse();

        user.Activate();
        user.IsActive.Should().BeTrue();
    }
}
