using FluentAssertions;
using PanelForge.Infrastructure.Services;
using Xunit;

namespace PanelForge.UnitTests.Infrastructure.Services;

public class AiProviderValidatorTests
{
    private readonly AiProviderValidator _validator = new();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ValidateCredentialsAsync_WhenKeyIsEmpty_ShouldReturnFailure(string? apiKey)
    {
        var result = await _validator.ValidateCredentialsAsync("Google Gemini", apiKey!);
        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("không được để trống");
    }

    [Fact]
    public async Task ValidateCredentialsAsync_WhenGeminiKeyTooShort_ShouldReturnFailure()
    {
        var result = await _validator.ValidateCredentialsAsync("Google Gemini", "short");
        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Google Gemini");
    }

    [Fact]
    public async Task ValidateCredentialsAsync_WhenGeminiKeyValid_ShouldReturnSuccess()
    {
        var result = await _validator.ValidateCredentialsAsync("Google Gemini", "AIzaSyValidGeminiKey12345");
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateCredentialsAsync_WhenOpenAiKeyMissingPrefix_ShouldReturnFailure()
    {
        var result = await _validator.ValidateCredentialsAsync("OpenAI", "not-starting-with-sk-12345");
        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("OpenAI");
    }

    [Fact]
    public async Task ValidateCredentialsAsync_WhenOpenAiKeyValid_ShouldReturnSuccess()
    {
        var result = await _validator.ValidateCredentialsAsync("OpenAI", "sk-proj-1234567890abcdef");
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateCredentialsAsync_WhenAnthropicKeyMissingPrefix_ShouldReturnFailure()
    {
        var result = await _validator.ValidateCredentialsAsync("Anthropic Claude", "sk-wrong-prefix-12345");
        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Anthropic/Claude");
    }

    [Fact]
    public async Task ValidateCredentialsAsync_WhenAnthropicKeyValid_ShouldReturnSuccess()
    {
        var result = await _validator.ValidateCredentialsAsync("Anthropic Claude", "sk-ant-api03-abcdefghijklmn");
        result.IsSuccess.Should().BeTrue();
    }
}
