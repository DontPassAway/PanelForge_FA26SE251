using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Auth.Models;
using PanelForge.Application.Features.Auth.Commands.ChangePassword;
using PanelForge.Application.Features.Auth.Commands.EnableTwoFactor;
using PanelForge.Application.Features.Auth.Commands.ExternalLogin;
using PanelForge.Application.Features.Auth.Commands.ForgetDevice;
using PanelForge.Application.Features.Auth.Commands.ForgotPassword;
using PanelForge.Application.Features.Auth.Commands.Login;
using PanelForge.Application.Features.Auth.Commands.Logout;
using PanelForge.Application.Features.Auth.Commands.RefreshToken;
using PanelForge.Application.Features.Auth.Commands.Register;
using PanelForge.Application.Features.Auth.Commands.ResendVerificationEmail;
using PanelForge.Application.Features.Auth.Commands.ResetPassword;
using PanelForge.Application.Features.Auth.Commands.SendChangePasswordOtp;
using PanelForge.Application.Features.Auth.Commands.VerifyEmail;
using PanelForge.Application.Features.Auth.Commands.VerifyTwoFactor;
using PanelForge.Application.Features.Auth.Queries.GetLandingContext;

namespace PanelForge.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private const string RememberDeviceCookie = "pf_remember_device";
    private const string RememberDeviceHeader = "X-Remember-Device-Token";

    private readonly IMediator _mediator;

    public AuthController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [EnableRateLimiting("auth")]
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new RegisterCommand(request.Email, request.Password, request.FullName, request.PhoneNumber),
            cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ToError(result);
    }

    [EnableRateLimiting("auth")]
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        var rememberDeviceToken = Request.Cookies[RememberDeviceCookie]
            ?? Request.Headers[RememberDeviceHeader].FirstOrDefault()
            ?? request.RememberDeviceToken;

        var result = await _mediator.Send(
            new LoginCommand(request.Email, request.Password, rememberDeviceToken), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ToError(result);
    }

    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout([FromBody] LogoutRequest? request, CancellationToken cancellationToken)
    {
        if (GetCurrentUserId() is { } userId)
            await _mediator.Send(new LogoutCommand(userId, request?.RefreshToken), cancellationToken);
        return Ok(new { message = "Đăng xuất thành công." });
    }

    /// <summary>Đổi refresh token lấy access JWT mới (xoay vòng refresh token).</summary>
    [EnableRateLimiting("auth")]
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] RefreshTokenRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new RefreshTokenCommand(request.RefreshToken), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ToError(result);
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> GetCurrentUser(CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");
        var email = User.FindFirstValue(ClaimTypes.Email)
            ?? User.FindFirstValue("email");
        var name = User.FindFirstValue(ClaimTypes.Name)
            ?? User.FindFirstValue("name");
        var role = User.FindFirstValue(ClaimTypes.Role)
            ?? User.FindFirstValue("role")
            ?? "User";

        // BR-22: CanCreateStudio đọc từ DB (có thể đã được Admin cấp/thu hồi sau khi token phát hành)
        var canCreateStudio = false;
        if (Guid.TryParse(userId, out var parsedUserId))
        {
            var context = await _mediator.Send(new GetLandingContextQuery(parsedUserId), cancellationToken);
            canCreateStudio = context.IsSuccess && context.Value!.CanCreateStudio;
        }

        return Ok(new
        {
            Id = userId,
            Email = email,
            FullName = name,
            Role = role,
            CanCreateStudio = canCreateStudio
        });
    }

    [Authorize]
    [HttpPost("send-change-password-otp")]
    public async Task<IActionResult> SendChangePasswordOtp(CancellationToken cancellationToken)
    {
        if (GetCurrentUserId() is not { } userId)
            return UnknownUser();

        var result = await _mediator.Send(new SendChangePasswordOtpCommand(userId), cancellationToken);
        return result.IsSuccess
            ? Ok(new { message = "Mã xác thực OTP đã được gửi đến email của bạn." })
            : ToError(result);
    }

    [Authorize]
    [EnableRateLimiting("auth")]
    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        if (GetCurrentUserId() is not { } userId)
            return UnknownUser();

        var result = await _mediator.Send(new ChangePasswordCommand(
            userId, request.CurrentPassword, request.NewPassword, request.EmailOtp, request.OtpCode,
            request.CurrentRefreshToken),
            cancellationToken);
        return result.IsSuccess
            ? Ok(new { message = "Đổi mật khẩu thành công." })
            : ToError(result);
    }

    [EnableRateLimiting("auth")]
    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request, CancellationToken cancellationToken)
    {
        await _mediator.Send(new ForgotPasswordCommand(request.Email), cancellationToken);
        return Ok(new { message = "Nếu email tồn tại trong hệ thống, mã xác thực đặt lại mật khẩu đã được gửi đến hộp thư của bạn." });
    }

    [EnableRateLimiting("auth")]
    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new ResetPasswordCommand(request.Email, request.Token, request.NewPassword), cancellationToken);
        return result.IsSuccess
            ? Ok(new { message = "Đặt lại mật khẩu thành công. Bạn hiện có thể đăng nhập bằng mật khẩu mới." })
            : ToError(result);
    }

    [EnableRateLimiting("auth")]
    [HttpPost("verify-email")]
    public async Task<IActionResult> VerifyEmail([FromBody] VerifyEmailRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new VerifyEmailCommand(request.Email, request.Token), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ToError(result);
    }

    [EnableRateLimiting("auth")]
    [HttpPost("resend-verification")]
    public async Task<IActionResult> ResendVerification([FromBody] ResendVerificationRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new ResendVerificationEmailCommand(request.Email), cancellationToken);
        return result.IsSuccess
            ? Ok(new { message = "Mã xác thực mới đã được gửi đến email của bạn nếu tài khoản hợp lệ." })
            : ToError(result);
    }

    [HttpPost("external-login")]
    public async Task<IActionResult> ExternalLogin([FromBody] ExternalLoginRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new ExternalLoginCommand(request.IdToken), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ToError(result);
    }

    [Authorize]
    [HttpPost("enable-2fa")]
    public async Task<IActionResult> EnableTwoFactor(CancellationToken cancellationToken)
    {
        if (GetCurrentUserId() is not { } userId)
            return UnknownUser();

        var result = await _mediator.Send(new EnableTwoFactorCommand(userId), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ToError(result);
    }

    [EnableRateLimiting("auth")]
    [HttpPost("verify-2fa")]
    public async Task<IActionResult> VerifyTwoFactor([FromBody] VerifyTwoFactorRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new VerifyTwoFactorCommand(
            GetCurrentUserId(), request.Code, request.Email, request.RememberDevice, request.DeviceName),
            cancellationToken);
        if (!result.IsSuccess)
            return ToError(result);

        var response = result.Value!;
        if (!string.IsNullOrEmpty(response.RememberDeviceToken))
        {
            Response.Cookies.Append(RememberDeviceCookie, response.RememberDeviceToken, new CookieOptions
            {
                HttpOnly = true,
                Secure = Request.IsHttps,
                SameSite = SameSiteMode.Lax,
                Expires = DateTimeOffset.UtcNow.AddDays(30)
            });
        }

        return Ok(response);
    }

    [HttpPost("forget-device")]
    public async Task<IActionResult> ForgetDevice(CancellationToken cancellationToken)
    {
        var rememberDeviceToken = Request.Cookies[RememberDeviceCookie]
            ?? Request.Headers[RememberDeviceHeader].FirstOrDefault();

        await _mediator.Send(new ForgetDeviceCommand(rememberDeviceToken, GetCurrentUserId()), cancellationToken);

        Response.Cookies.Delete(RememberDeviceCookie);

        return Ok(new { message = "Thiết bị này đã được xóa khỏi danh sách thiết bị tin cậy." });
    }

    /// <summary>
    /// GET /api/auth/me/landing-context
    /// BR-23: Returns the authenticated user's system role, CanCreateStudio, workspace memberships
    /// and a suggested landing screen (AdminDashboard|YourWorkspaces|StudioCreation|PublicCatalog).
    /// UserId is always resolved from the JWT token, never from the request body.
    /// </summary>
    [Authorize]
    [HttpGet("me/landing-context")]
    public async Task<IActionResult> GetLandingContext(CancellationToken cancellationToken)
    {
        if (GetCurrentUserId() is not { } userId)
            return UnknownUser();

        var result = await _mediator.Send(new GetLandingContextQuery(userId), cancellationToken);
        if (!result.IsSuccess)
            return NotFound(new { message = result.ErrorMessage });

        return Ok(result.Value);
    }

    private Guid? GetCurrentUserId()
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(userIdStr, out var userId) ? userId : null;
    }

    private IActionResult UnknownUser()
        => Unauthorized(new { message = "Không xác định được danh tính người dùng." });

    // UNAUTHORIZED → 401, lỗi nghiệp vụ khác → 400; body { message } như trước
    private IActionResult ToError<T>(Result<T> result)
        => result.ErrorCode == ResultErrorCodes.Unauthorized
            ? Unauthorized(new { message = result.ErrorMessage })
            : BadRequest(new { message = result.ErrorMessage });
}
