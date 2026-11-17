using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using PanelForge.API.Controllers;
using PanelForge.Application.Common.Interfaces.Services;
using Xunit;

namespace PanelForge.UnitTests.API;

public class TranslationsControllerTests
{
    private readonly Mock<ITranslationService> _mockTranslationService;
    private readonly TranslationsController _controller;

    public TranslationsControllerTests()
    {
        _mockTranslationService = new Mock<ITranslationService>();
        _controller = new TranslationsController(_mockTranslationService.Object);
    }

    [Fact]
    public async Task Translate_ValidRequest_ReturnsOkWithTranslatedText()
    {
        _mockTranslationService
            .Setup(s => s.TranslateAsync("Hello", "en", "vi", It.IsAny<CancellationToken>()))
            .ReturnsAsync("Xin chào");

        var request = new TranslateRequest("Hello", "en", "vi");
        var result = await _controller.Translate(request, CancellationToken.None);

        var okResult = result as OkObjectResult;
        okResult.Should().NotBeNull();
        okResult!.StatusCode.Should().Be(200);

        var response = okResult.Value as TranslateResponse;
        response.Should().NotBeNull();
        response!.TranslatedText.Should().Be("Xin chào");
        response.SourceLanguage.Should().Be("en");
        response.TargetLanguage.Should().Be("vi");
    }

    [Theory]
    [InlineData("EN", "VI")]
    [InlineData("En", "Vi")]
    public async Task Translate_CaseInsensitiveLanguages_NormalizesToLower(string source, string target)
    {
        _mockTranslationService
            .Setup(s => s.TranslateAsync("Text", "en", "vi", It.IsAny<CancellationToken>()))
            .ReturnsAsync("Văn bản");

        var request = new TranslateRequest("Text", source, target);
        var result = await _controller.Translate(request, CancellationToken.None);

        var okResult = result as OkObjectResult;
        okResult.Should().NotBeNull();

        var response = okResult!.Value as TranslateResponse;
        response.Should().NotBeNull();
        response!.SourceLanguage.Should().Be("en");
        response.TargetLanguage.Should().Be("vi");
    }

    [Fact]
    public async Task Translate_NullRequest_ReturnsBadRequest()
    {
        var result = await _controller.Translate(null, CancellationToken.None);

        var badRequest = result as BadRequestObjectResult;
        badRequest.Should().NotBeNull();
        badRequest!.StatusCode.Should().Be(400);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Translate_EmptyOrWhitespaceText_ReturnsBadRequest(string? text)
    {
        var request = new TranslateRequest(text, "en", "vi");
        var result = await _controller.Translate(request, CancellationToken.None);

        var badRequest = result as BadRequestObjectResult;
        badRequest.Should().NotBeNull();
        badRequest!.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Translate_TextExceedingMaxLength_ReturnsBadRequest()
    {
        var longText = new string('a', 4001);
        var request = new TranslateRequest(longText, "en", "vi");
        var result = await _controller.Translate(request, CancellationToken.None);

        var badRequest = result as BadRequestObjectResult;
        badRequest.Should().NotBeNull();
        badRequest!.StatusCode.Should().Be(400);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("fr")]
    [InlineData("es")]
    [InlineData("zh")]
    public async Task Translate_InvalidSourceLanguage_ReturnsBadRequest(string? sourceLang)
    {
        var request = new TranslateRequest("Hello", sourceLang, "vi");
        var result = await _controller.Translate(request, CancellationToken.None);

        var badRequest = result as BadRequestObjectResult;
        badRequest.Should().NotBeNull();
        badRequest!.StatusCode.Should().Be(400);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ja")]
    [InlineData("ko")]
    [InlineData("de")]
    public async Task Translate_InvalidTargetLanguage_ReturnsBadRequest(string? targetLang)
    {
        var request = new TranslateRequest("Hello", "en", targetLang);
        var result = await _controller.Translate(request, CancellationToken.None);

        var badRequest = result as BadRequestObjectResult;
        badRequest.Should().NotBeNull();
        badRequest!.StatusCode.Should().Be(400);
    }
}
