using FluentAssertions;
using PanelForge.Infrastructure.Authentication;
using Xunit;

namespace PanelForge.UnitTests.Infrastructure.Authentication;

public class BcryptPasswordHasherTests
{
    private readonly BcryptPasswordHasher _hasher = new();

    [Fact]
    public void HashPassword_ShouldGenerateValidBcryptHash()
    {
        // Arrange
        const string password = "MySecurePassword123!";

        // Act
        var hash = _hasher.HashPassword(password);

        // Assert
        hash.Should().NotBeNullOrWhiteSpace();
        hash.Should().StartWith("$2"); // BCrypt signature prefix
        hash.Should().NotBe(password);
    }

    [Fact]
    public void VerifyPassword_MatchingPassword_ShouldReturnTrue()
    {
        // Arrange
        const string password = "MySecurePassword123!";
        var hash = _hasher.HashPassword(password);

        // Act
        var isValid = _hasher.VerifyPassword(password, hash);

        // Assert
        isValid.Should().BeTrue();
    }

    [Fact]
    public void VerifyPassword_WrongPassword_ShouldReturnFalse()
    {
        // Arrange
        const string password = "MySecurePassword123!";
        var hash = _hasher.HashPassword(password);

        // Act
        var isValid = _hasher.VerifyPassword("WrongPassword456!", hash);

        // Assert
        isValid.Should().BeFalse();
    }
}
