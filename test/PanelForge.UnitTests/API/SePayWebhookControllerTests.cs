using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Moq;
using PanelForge.API.Controllers;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Payments.Commands.ProcessSePayWebhook;
using Xunit;

namespace PanelForge.UnitTests.API;

/// <summary>Webhook SePay chỉ nhận request mang đúng header <c>Authorization: Apikey &lt;key&gt;</c>.</summary>
public class SePayWebhookControllerTests
{
    private const string Key = "test-sepay-key";
    private readonly Mock<IMediator> _mediator = new();

    private static readonly SePayWebhookPayload Payload = new(1, "MBBank", null, "0123456789", null, "PF123456",
        "in", 2000, 0, null, null, null);

    private SePayWebhookController Controller(string? configuredKey, string? authorization)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["SePay:WebhookApiKey"] = configuredKey })
            .Build();
        var http = new DefaultHttpContext();
        if (authorization is not null) http.Request.Headers.Authorization = authorization;
        return new SePayWebhookController(_mediator.Object, config) { ControllerContext = new ControllerContext { HttpContext = http } };
    }

    [Fact]
    public async Task NoKeyConfigured_Returns503_WithoutProcessing()
    {
        var result = await Controller(null, $"Apikey {Key}").Receive(Payload, default);

        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
        _mediator.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Apikey wrong-key")]
    [InlineData($"Bearer {Key}")]
    [InlineData(Key)]
    public async Task MissingOrWrongKey_Returns401_WithoutProcessing(string? authorization)
    {
        var result = await Controller(Key, authorization).Receive(Payload, default);

        result.Should().BeOfType<UnauthorizedObjectResult>();
        _mediator.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData($"Apikey {Key}")]
    [InlineData($"apikey  {Key} ")]
    public async Task CorrectKey_ProcessesAndReturns200(string authorization)
    {
        _mediator.Setup(m => m.Send(It.IsAny<ProcessSePayWebhookCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<SePayWebhookOutcome>.Success(SePayWebhookOutcome.IgnoredOrderNotFound));

        var result = await Controller(Key, authorization).Receive(Payload, default);

        result.Should().BeOfType<OkObjectResult>();
        _mediator.Verify(m => m.Send(It.Is<ProcessSePayWebhookCommand>(c => c.Payload == Payload), It.IsAny<CancellationToken>()), Times.Once);
    }
}
