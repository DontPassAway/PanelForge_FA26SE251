using MediatR;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using PanelForge.API.Authentication;
using PanelForge.API.Controllers;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Auth.Commands.Login;
using PanelForge.Application.Features.Auth.Commands.VerifyTwoFactor;
using PanelForge.Application.Features.Auth.Models;

namespace PanelForge.UnitTests.API;

/// <summary>Đăng nhập 2FA phải gắn với bước mật khẩu: verify-2fa chỉ nhận thử thách do chính server cấp ở /login.</summary>
public class TwoFactorChallengeCookieTests
{
    private static readonly IServiceProvider Services = new ServiceCollection()
        .AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider())
        .BuildServiceProvider();

    private static DefaultHttpContext Context(string? cookieHeader = null)
    {
        var ctx = new DefaultHttpContext { RequestServices = Services };
        if (cookieHeader is not null)
            ctx.Request.Headers.Cookie = cookieHeader;
        return ctx;
    }

    /// <summary>Cặp "tên=giá trị" của cookie pf_2fa vừa được set trên response, để gửi lại ở request sau.</summary>
    private static string? ChallengeCookie(HttpContext ctx)
        => ctx.Response.Headers.SetCookie.ToArray()
            .FirstOrDefault(h => h!.StartsWith(TwoFactorChallengeCookie.Name + "="))?.Split(';')[0];

    private static AuthController Controller(Mock<IMediator> mediator, HttpContext ctx)
        => new(mediator.Object) { ControllerContext = new() { HttpContext = ctx } };

    [Fact]
    public void SetThenRead_RoundTripsEmail_AsHttpOnlyCookieScopedToAuth()
    {
        var first = Context();
        TwoFactorChallengeCookie.Set(first, "user@example.com");

        var header = Assert.Single(first.Response.Headers.SetCookie.ToArray())!;
        Assert.Contains("httponly", header, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/api/auth", header, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("user@example.com", header);

        Assert.Equal("user@example.com", TwoFactorChallengeCookie.Read(Context(ChallengeCookie(first))));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("pf_2fa=forged-value")]
    public void Read_WithoutCookieOrWithForgedValue_ReturnsNull(string? cookieHeader)
        => Assert.Null(TwoFactorChallengeCookie.Read(Context(cookieHeader)));

    [Fact]
    public async Task Login_WhenTwoFactorRequired_IssuesChallenge_ThatVerifyTwoFactorConsumes()
    {
        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<LoginCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AuthResponse>.Success(
                new AuthResponse(RequiresTwoFactor: true, TwoFactorEmail: "user@example.com")));
        mediator.Setup(m => m.Send(It.IsAny<VerifyTwoFactorCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AuthResponse>.Success(
                new AuthResponse(Token: "jwt-x", ExpiresAt: DateTime.UtcNow.AddMinutes(30), RefreshToken: "rt-x")));

        var loginCtx = Context();
        await Controller(mediator, loginCtx).Login(new LoginRequest("user@example.com", "pw"), CancellationToken.None);
        var challenge = ChallengeCookie(loginCtx);
        Assert.NotNull(challenge);

        var verifyCtx = Context(challenge);
        var result = await Controller(mediator, verifyCtx).VerifyTwoFactor(
            new VerifyTwoFactorRequest("123456", "user@example.com", false, null), CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        mediator.Verify(m => m.Send(
            It.Is<VerifyTwoFactorCommand>(c => c.ChallengeEmail == "user@example.com"), It.IsAny<CancellationToken>()));
        // Thử thách dùng một lần: đăng nhập xong thì xoá cookie.
        Assert.Contains(verifyCtx.Response.Headers.SetCookie.ToArray(),
            h => h!.StartsWith("pf_2fa=;") && h.Contains("expires=", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task VerifyTwoFactor_WithoutChallengeCookie_PassesNoChallengeToHandler()
    {
        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<VerifyTwoFactorCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AuthResponse>.Failure("Chưa qua bước đăng nhập.", ResultErrorCodes.Unauthorized));

        var result = await Controller(mediator, Context()).VerifyTwoFactor(
            new VerifyTwoFactorRequest("123456", "user@example.com", false, null), CancellationToken.None);

        Assert.IsType<UnauthorizedObjectResult>(result);
        mediator.Verify(m => m.Send(
            It.Is<VerifyTwoFactorCommand>(c => c.ChallengeEmail == null), It.IsAny<CancellationToken>()));
    }
}
