using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using PanelForge.Infrastructure.Services;
using Xunit;

namespace PanelForge.UnitTests.Infrastructure.Services;

public class AiKeyProtectorTests
{
    private readonly AiKeyProtector _protector;

    public AiKeyProtectorTests()
    {
        var provider = new EphemeralDataProtectionProvider();
        _protector = new AiKeyProtector(provider);
    }

    [Fact]
    public void Protect_WhenRawKeyProvided_ShouldReturnEncryptedStringDifferentFromRaw()
    {
        const string rawKey = "AIzaSyTestKey123456789";
        var protectedKey = _protector.Protect(rawKey);

        protectedKey.Should().NotBeNullOrWhiteSpace();
        protectedKey.Should().NotBe(rawKey);
    }

    [Fact]
    public void Unprotect_WhenProtectedStringProvided_ShouldRecoverOriginalKey()
    {
        const string rawKey = "sk-proj-secret-key-abcdef123456";
        var protectedKey = _protector.Protect(rawKey);
        var recoveredKey = _protector.Unprotect(protectedKey);

        recoveredKey.Should().Be(rawKey);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Protect_WhenNullOrWhitespace_ShouldReturnEmpty(string? rawKey)
    {
        var result = _protector.Protect(rawKey!);
        result.Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Unprotect_WhenNullOrWhitespace_ShouldReturnEmpty(string? protectedKey)
    {
        var result = _protector.Unprotect(protectedKey!);
        result.Should().BeEmpty();
    }

    [Fact]
    public void Unprotect_WhenInvalidCiphertext_ShouldReturnEmpty_WithoutThrowing()
    {
        var result = _protector.Unprotect("corrupted-ciphertext-12345");
        result.Should().BeEmpty();
    }
}
