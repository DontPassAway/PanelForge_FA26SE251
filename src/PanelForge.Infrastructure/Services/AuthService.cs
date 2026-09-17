using System.Security.Cryptography;
using System.Text;
using FirebaseAdmin.Auth;
using Microsoft.EntityFrameworkCore;
using OtpNet;
using PanelForge.Application.DTOs.Auth;
using PanelForge.Application.Interfaces;
using PanelForge.Application.Interfaces.Authentication;
using PanelForge.Application.Interfaces.Persistence;
using PanelForge.Application.Services;
using PanelForge.Domain.Entities.Auth;

namespace PanelForge.Infrastructure.Services;

public class AuthService : IAuthService
{
    private readonly IPanelForgeDbContext _dbContext;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenGenerator _jwtTokenGenerator;
    private readonly IEmailService _emailService;

    public AuthService(
        IPanelForgeDbContext dbContext,
        IPasswordHasher passwordHasher,
        IJwtTokenGenerator jwtTokenGenerator,
        IEmailService emailService)
    {
        _dbContext = dbContext;
        _passwordHasher = passwordHasher;
        _jwtTokenGenerator = jwtTokenGenerator;
        _emailService = emailService;
    }

    public async Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        var existingUser = await _dbContext.Users
            .AnyAsync(u => u.Email == normalizedEmail, cancellationToken);

        if (existingUser)
        {
            throw new InvalidOperationException("Email đã được đăng ký trong hệ thống.");
        }

        var passwordHash = _passwordHasher.HashPassword(request.Password);

        var user = User.Create(
            email: normalizedEmail,
            fullName: request.FullName,
            passwordHash: passwordHash,
            phoneNumber: request.PhoneNumber
        );

        // Tạo mã OTP 6 số để xác thực email
        var verificationOtp = RandomNumberGenerator.GetInt32(100000, 999999).ToString();
        user.SetEmailVerificationToken(verificationOtp, DateTime.UtcNow.AddHours(24));

        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync(cancellationToken);

        // Gửi email xác thực
        await _emailService.SendEmailVerificationAsync(user.Email, verificationOtp, user.FullName, cancellationToken);

        var (token, expiresAt) = _jwtTokenGenerator.GenerateToken(user);

        return new AuthResponse(
            Token: token,
            ExpiresAt: expiresAt,
            User: MapUserDto(user),
            Message: "Đăng ký tài khoản thành công. Vui lòng kiểm tra email để xác thực tài khoản."
        );
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        var user = await _dbContext.Users
            .FirstOrDefaultAsync(u => u.Email == normalizedEmail, cancellationToken);

        if (user is null || !user.IsActive || string.IsNullOrEmpty(user.PasswordHash))
        {
            throw new UnauthorizedAccessException("Email hoặc mật khẩu không chính xác.");
        }

        var isValidPassword = _passwordHasher.VerifyPassword(request.Password, user.PasswordHash);
        if (!isValidPassword)
        {
            throw new UnauthorizedAccessException("Email hoặc mật khẩu không chính xác.");
        }

        // Nếu người dùng đã bật bảo mật 2FA
        if (user.TwoFactorEnabled)
        {
            return new AuthResponse(
                Token: null,
                ExpiresAt: null,
                User: null,
                RequiresTwoFactor: true,
                TwoFactorEmail: user.Email,
                Message: "Yêu cầu xác thực 2FA. Vui lòng nhập mã OTP 6 chữ số từ ứng dụng Authenticator."
            );
        }

        var (token, expiresAt) = _jwtTokenGenerator.GenerateToken(user);

        return new AuthResponse(
            Token: token,
            ExpiresAt: expiresAt,
            User: MapUserDto(user),
            Message: "Đăng nhập thành công."
        );
    }

    public async Task ChangePasswordAsync(Guid userId, ChangePasswordRequest request, CancellationToken cancellationToken = default)
    {
        var user = await _dbContext.Users
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user is null || !user.IsActive)
        {
            throw new UnauthorizedAccessException("Người dùng không tồn tại hoặc đã bị vô hiệu hóa.");
        }

        if (string.IsNullOrEmpty(user.PasswordHash))
        {
            throw new InvalidOperationException("Tài khoản chưa được thiết lập mật khẩu (được tạo qua đăng nhập mạng xã hội).");
        }

        var isOldPasswordCorrect = _passwordHasher.VerifyPassword(request.CurrentPassword, user.PasswordHash);
        if (!isOldPasswordCorrect)
        {
            throw new UnauthorizedAccessException("Mật khẩu hiện tại không chính xác.");
        }

        if (_passwordHasher.VerifyPassword(request.NewPassword, user.PasswordHash))
        {
            throw new InvalidOperationException("Mật khẩu mới không được trùng với mật khẩu hiện tại.");
        }

        var newPasswordHash = _passwordHasher.HashPassword(request.NewPassword);
        user.UpdatePassword(newPasswordHash);

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        var user = await _dbContext.Users
            .FirstOrDefaultAsync(u => u.Email == normalizedEmail, cancellationToken);

        // Để tránh Email Enumeration Attack, nếu user không tồn tại ta vẫn trả về thành công an toàn
        if (user is null || !user.IsActive)
        {
            return;
        }

        var resetOtp = RandomNumberGenerator.GetInt32(100000, 999999).ToString();
        user.SetPasswordResetToken(resetOtp, DateTime.UtcNow.AddMinutes(15));

        await _dbContext.SaveChangesAsync(cancellationToken);
        await _emailService.SendPasswordResetEmailAsync(user.Email, resetOtp, user.FullName, cancellationToken);
    }

    public async Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        var user = await _dbContext.Users
            .FirstOrDefaultAsync(u => u.Email == normalizedEmail, cancellationToken);

        if (user is null || !user.IsActive)
        {
            throw new ArgumentException("Email hoặc mã xác thực không hợp lệ.");
        }

        if (string.IsNullOrEmpty(user.PasswordResetToken) || !string.Equals(user.PasswordResetToken, request.Token.Trim(), StringComparison.Ordinal))
        {
            throw new ArgumentException("Mã xác thực đặt lại mật khẩu không chính xác.");
        }

        if (!user.PasswordResetTokenExpiresAt.HasValue || user.PasswordResetTokenExpiresAt.Value < DateTime.UtcNow)
        {
            throw new ArgumentException("Mã xác thực đặt lại mật khẩu đã hết hạn. Vui lòng yêu cầu mã mới.");
        }

        var newPasswordHash = _passwordHasher.HashPassword(request.NewPassword);
        user.ResetPassword(newPasswordHash);

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task VerifyEmailAsync(VerifyEmailRequest request, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        var user = await _dbContext.Users
            .FirstOrDefaultAsync(u => u.Email == normalizedEmail, cancellationToken);

        if (user is null || !user.IsActive)
        {
            throw new ArgumentException("Tài khoản không tồn tại hoặc đã bị vô hiệu hóa.");
        }

        if (user.IsEmailConfirmed)
        {
            return; // Đã xác thực trước đó
        }

        if (string.IsNullOrEmpty(user.EmailVerificationToken) || !string.Equals(user.EmailVerificationToken, request.Token.Trim(), StringComparison.Ordinal))
        {
            throw new ArgumentException("Mã xác thực email không chính xác.");
        }

        if (!user.EmailVerificationTokenExpiresAt.HasValue || user.EmailVerificationTokenExpiresAt.Value < DateTime.UtcNow)
        {
            throw new ArgumentException("Mã xác thực email đã hết hạn. Vui lòng yêu cầu gửi lại mã mới.");
        }

        user.ConfirmEmail();
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task ResendVerificationEmailAsync(ResendVerificationRequest request, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        var user = await _dbContext.Users
            .FirstOrDefaultAsync(u => u.Email == normalizedEmail, cancellationToken);

        if (user is null || !user.IsActive)
        {
            return;
        }

        if (user.IsEmailConfirmed)
        {
            throw new InvalidOperationException("Tài khoản email này đã được xác thực trước đó.");
        }

        var verificationOtp = RandomNumberGenerator.GetInt32(100000, 999999).ToString();
        user.SetEmailVerificationToken(verificationOtp, DateTime.UtcNow.AddHours(24));

        await _dbContext.SaveChangesAsync(cancellationToken);
        await _emailService.SendEmailVerificationAsync(user.Email, verificationOtp, user.FullName, cancellationToken);
    }

    public async Task<AuthResponse> ExternalLoginAsync(ExternalLoginRequest request, CancellationToken cancellationToken = default)
    {
        FirebaseToken decodedToken;
        try
        {
            var auth = FirebaseAuth.DefaultInstance;
            if (auth is null)
            {
                throw new InvalidOperationException("FirebaseApp chưa được khởi tạo. Vui lòng cấu hình Firebase trong appsettings.json.");
            }

            decodedToken = await auth.VerifyIdTokenAsync(request.IdToken, cancellationToken);
        }
        catch (FirebaseAuthException ex)
        {
            throw new UnauthorizedAccessException($"Xác thực Firebase ID Token thất bại: {ex.Message}");
        }

        var firebaseUid = decodedToken.Uid;
        string? email = null;
        if (decodedToken.Claims.TryGetValue("email", out var emailClaim) && emailClaim is not null)
        {
            email = emailClaim.ToString();
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            throw new InvalidOperationException("Firebase Token không chứa thông tin email hợp lệ.");
        }

        var normalizedEmail = email.Trim().ToLowerInvariant();
        string? name = decodedToken.Claims.TryGetValue("name", out var nameClaim) ? nameClaim?.ToString() : null;
        string? picture = decodedToken.Claims.TryGetValue("picture", out var picClaim) ? picClaim?.ToString() : null;

        var user = await _dbContext.Users
            .FirstOrDefaultAsync(u => u.FirebaseUid == firebaseUid || u.Email == normalizedEmail, cancellationToken);

        if (user is null)
        {
            user = User.Create(
                email: normalizedEmail,
                fullName: !string.IsNullOrWhiteSpace(name) ? name : normalizedEmail.Split('@')[0],
                passwordHash: null,
                firebaseUid: firebaseUid,
                avatarUrl: picture
            );
            user.ConfirmEmail();
            _dbContext.Users.Add(user);
        }
        else
        {
            if (!user.IsActive)
            {
                throw new UnauthorizedAccessException("Tài khoản này đã bị vô hiệu hóa.");
            }

            if (string.IsNullOrEmpty(user.FirebaseUid))
            {
                user.LinkFirebase(firebaseUid);
            }

            if (!user.IsEmailConfirmed)
            {
                user.ConfirmEmail();
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        // Kiểm tra 2FA nếu tài khoản đã bật
        if (user.TwoFactorEnabled)
        {
            return new AuthResponse(
                Token: null,
                ExpiresAt: null,
                User: null,
                RequiresTwoFactor: true,
                TwoFactorEmail: user.Email,
                Message: "Yêu cầu xác thực 2FA. Vui lòng nhập mã OTP 6 chữ số từ ứng dụng Authenticator."
            );
        }

        var (jwtToken, expiresAt) = _jwtTokenGenerator.GenerateToken(user);

        return new AuthResponse(
            Token: jwtToken,
            ExpiresAt: expiresAt,
            User: MapUserDto(user),
            Message: "Đăng nhập mạng xã hội thành công."
        );
    }

    public async Task<EnableTwoFactorResponse> EnableTwoFactorAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await _dbContext.Users
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user is null || !user.IsActive)
        {
            throw new UnauthorizedAccessException("Người dùng không tồn tại hoặc đã bị vô hiệu hóa.");
        }

        // Sinh Secret Key 20 bytes ngẫu nhiên
        var secretKey = KeyGeneration.GenerateRandomKey(20);
        var base32Secret = Base32Encoding.ToString(secretKey);

        user.SetTwoFactorSecret(base32Secret);
        await _dbContext.SaveChangesAsync(cancellationToken);

        // Tạo URI chuẩn cho Google Authenticator / Microsoft Authenticator
        var appTitle = "PanelForge";
        var authenticatorUri = $"otpauth://totp/{appTitle}:{Uri.EscapeDataString(user.Email)}?secret={base32Secret}&issuer={appTitle}&digits=6&period=30";

        // Định dạng key thành từng cụm 4 chữ cái cho người dùng dễ nhập thủ công
        var formattedKey = FormatKey(base32Secret);

        return new EnableTwoFactorResponse(
            SharedKey: base32Secret,
            AuthenticatorUri: authenticatorUri,
            FormattedKey: formattedKey
        );
    }

    public async Task<AuthResponse> VerifyTwoFactorAsync(Guid? currentUserId, VerifyTwoFactorRequest request, CancellationToken cancellationToken = default)
    {
        User? user;

        if (currentUserId.HasValue)
        {
            // Trường hợp 1: Người dùng đang đăng nhập và muốn xác nhận mã 6 số để kích hoạt 2FA
            user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == currentUserId.Value, cancellationToken);
            if (user is null || !user.IsActive)
            {
                throw new UnauthorizedAccessException("Người dùng không tồn tại hoặc đã bị vô hiệu hóa.");
            }

            if (string.IsNullOrEmpty(user.TwoFactorSecret))
            {
                throw new InvalidOperationException("Chưa khởi tạo khóa bảo mật 2FA. Vui lòng gọi enable-2fa trước.");
            }

            var secretBytes = Base32Encoding.ToBytes(user.TwoFactorSecret);
            var totp = new Totp(secretBytes);
            var isCodeValid = totp.VerifyTotp(request.Code.Trim(), out _, new VerificationWindow(previous: 1, future: 1));

            if (!isCodeValid)
            {
                throw new UnauthorizedAccessException("Mã xác thực 2FA không chính xác.");
            }

            user.EnableTwoFactor();
            await _dbContext.SaveChangesAsync(cancellationToken);

            return new AuthResponse(
                Token: null,
                ExpiresAt: null,
                User: MapUserDto(user),
                Message: "Bảo mật 2FA đã được kích hoạt thành công cho tài khoản của bạn."
            );
        }
        else
        {
            // Trường hợp 2: Người dùng đang trong bước đăng nhập (Challenge 2FA)
            if (string.IsNullOrWhiteSpace(request.Email))
            {
                throw new ArgumentException("Email là bắt buộc khi xác thực 2FA đăng nhập.");
            }

            var normalizedEmail = request.Email.Trim().ToLowerInvariant();
            user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Email == normalizedEmail, cancellationToken);

            if (user is null || !user.IsActive || !user.TwoFactorEnabled || string.IsNullOrEmpty(user.TwoFactorSecret))
            {
                throw new UnauthorizedAccessException("Thông tin tài khoản hoặc cấu hình 2FA không hợp lệ.");
            }

            var secretBytes = Base32Encoding.ToBytes(user.TwoFactorSecret);
            var totp = new Totp(secretBytes);
            var isCodeValid = totp.VerifyTotp(request.Code.Trim(), out _, new VerificationWindow(previous: 1, future: 1));

            if (!isCodeValid)
            {
                throw new UnauthorizedAccessException("Mã xác thực 2FA không chính xác.");
            }

            var (jwtToken, expiresAt) = _jwtTokenGenerator.GenerateToken(user);

            return new AuthResponse(
                Token: jwtToken,
                ExpiresAt: expiresAt,
                User: MapUserDto(user),
                Message: "Xác thực 2FA và đăng nhập thành công."
            );
        }
    }

    private static UserDto MapUserDto(User user)
    {
        return new UserDto(
            user.Id,
            user.Email,
            user.FullName,
            user.PhoneNumber,
            user.AvatarUrl,
            user.IsActive,
            user.IsEmailConfirmed,
            user.TwoFactorEnabled,
            user.Role.ToString()
        );
    }

    private static string FormatKey(string key)
    {
        var sb = new StringBuilder();
        int currentPosition = 0;
        while (currentPosition + 4 < key.Length)
        {
            sb.Append(key.AsSpan(currentPosition, 4)).Append(' ');
            currentPosition += 4;
        }
        if (currentPosition < key.Length)
        {
            sb.Append(key.AsSpan(currentPosition));
        }
        return sb.ToString().ToLowerInvariant();
    }
}
