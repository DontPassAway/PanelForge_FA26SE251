using MediatR;
using Microsoft.AspNetCore.Http;
using Moq;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Auth.Commands.Login;
using PanelForge.Application.Features.Auth.Commands.VerifyEmail;
using Microsoft.AspNetCore.Mvc;
using PanelForge.API.Authentication;
using PanelForge.API.Controllers;
using PanelForge.Application.Features.Auth.Models;

namespace PanelForge.UnitTests.API;

public class AuthCookiesTests
{
    private static string[] SetCookieHeaders(HttpContext ctx) => ctx.Response.Headers.SetCookie.ToArray()!;

    [Fact]
    public void Set_WritesHttpOnlyLaxCookies_WithScopedPaths()
    {
        var ctx = new DefaultHttpContext();
        AuthCookies.Set(ctx.Request, ctx.Response,
            new AuthResponse(Token: "jwt-x", ExpiresAt: DateTime.UtcNow.AddMinutes(30), RefreshToken: "rt-x"));

        var headers = SetCookieHeaders(ctx);
        var access = Assert.Single(headers, h => h.StartsWith("pf_access=jwt-x"));
        var refresh = Assert.Single(headers, h => h.StartsWith("pf_refresh=rt-x"));
        foreach (var h in new[] { access, refresh })
        {
            Assert.Contains("httponly", h, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("samesite=lax", h, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("secure", h, StringComparison.OrdinalIgnoreCase); // http dev
        }
        Assert.Contains("path=/api;", access + ";");
        Assert.Contains("path=/api;", refresh + ";");
    }

    [Fact]
    public void Set_OverHttps_MarksSecure()
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Scheme = "https";
        AuthCookies.Set(ctx.Request, ctx.Response, new AuthResponse(Token: "jwt-x"));
        Assert.Contains("secure", Assert.Single(SetCookieHeaders(ctx)), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Set_TwoFactorPending_WritesNoCookie()
    {
        var ctx = new DefaultHttpContext();
        AuthCookies.Set(ctx.Request, ctx.Response, new AuthResponse(RequiresTwoFactor: true));
        Assert.Empty(SetCookieHeaders(ctx));
    }

    [Fact]
    public void Clear_ExpiresBothCookies()
    {
        var ctx = new DefaultHttpContext();
        AuthCookies.Clear(ctx.Response);
        var headers = SetCookieHeaders(ctx);
        Assert.Contains(headers, h => h.StartsWith("pf_access=;") && h.Contains("expires=Thu, 01 Jan 1970"));
        Assert.Contains(headers, h => h.StartsWith("pf_refresh=;") && (h + ";").Contains("path=/api;"));
    }

    [Fact]
    public async Task Login_Success_SetsCookies_AndBodyHasNoTokens()
    {
        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<LoginCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AuthResponse>.Success(
                new AuthResponse(Token: "jwt-x", ExpiresAt: DateTime.UtcNow.AddMinutes(30), RefreshToken: "rt-x")));
        var ctx = new DefaultHttpContext();
        var controller = new AuthController(mediator.Object) { ControllerContext = new() { HttpContext = ctx } };

        var result = await controller.Login(new LoginRequest("a@b.c", "pw"), CancellationToken.None);

        var body = Assert.IsType<AuthResponse>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Null(body.Token);
        Assert.Null(body.RefreshToken);
        Assert.NotNull(body.ExpiresAt);
        var headers = SetCookieHeaders(ctx);
        Assert.Contains(headers, h => h.StartsWith("pf_access=jwt-x"));
        Assert.Contains(headers, h => h.StartsWith("pf_refresh=rt-x"));
    }

    [Fact]
    public async Task VerifyEmail_Success_SignsInLikeLogin_CookiesSet_AndBodyHasNoTokens()
    {
        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<VerifyEmailCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AuthResponse>.Success(
                new AuthResponse(Token: "jwt-x", ExpiresAt: DateTime.UtcNow.AddMinutes(30), RefreshToken: "rt-x")));
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers.UserAgent = "Mozilla/5.0 (Windows NT 10.0) Chrome/129.0";
        var controller = new AuthController(mediator.Object) { ControllerContext = new() { HttpContext = ctx } };

        var result = await controller.VerifyEmail(new VerifyEmailRequest("a@b.c", "123456"), CancellationToken.None);

        var body = Assert.IsType<AuthResponse>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Null(body.Token);
        Assert.Null(body.RefreshToken);
        var headers = SetCookieHeaders(ctx);
        Assert.Contains(headers, h => h.StartsWith("pf_access=jwt-x"));
        Assert.Contains(headers, h => h.StartsWith("pf_refresh=rt-x"));
        mediator.Verify(m => m.Send(
            It.Is<VerifyEmailCommand>(c => c.UserAgent!.Contains("Chrome")), It.IsAny<CancellationToken>()));
    }

    [Theory]
    [InlineData("X-Requested-With", "XMLHttpRequest", true)]
    [InlineData("Sec-Fetch-Site", "same-origin", true)]
    [InlineData("Sec-Fetch-Site", "cross-site", false)]
    [InlineData("Sec-Fetch-Site", "same-site", false)]
    public void HasCsrfHeader(string header, string value, bool expected)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers[header] = value;
        Assert.Equal(expected, AuthCookies.HasCsrfHeader(ctx.Request));
    }

    [Fact]
    public async Task Refresh_FromCookieWithoutCsrfHeader_Returns401_WithoutTouchingTokens()
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers.Cookie = "pf_refresh=rt-x";
        var controller = new AuthController(mediator: null!) { ControllerContext = new() { HttpContext = ctx } };

        var result = await controller.Refresh(request: null, CancellationToken.None);

        Assert.IsType<UnauthorizedObjectResult>(result);
    }
}
