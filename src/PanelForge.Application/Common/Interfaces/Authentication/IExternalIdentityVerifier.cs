namespace PanelForge.Application.Common.Interfaces.Authentication;

/// <summary>Xác thực ID token của nhà cung cấp đăng nhập ngoài (hiện tại: Firebase).</summary>
public interface IExternalIdentityVerifier
{
    Task<ExternalIdentityResult> VerifyAsync(string idToken, CancellationToken cancellationToken = default);
}

public enum ExternalIdentityStatus
{
    Valid,
    /// <summary>Token sai / hết hạn.</summary>
    InvalidToken,
    /// <summary>Nhà cung cấp chưa được cấu hình trên server.</summary>
    NotConfigured
}

public sealed record ExternalIdentityResult(
    ExternalIdentityStatus Status,
    string? Uid = null,
    string? Email = null,
    string? Name = null,
    string? PictureUrl = null,
    string? ErrorMessage = null);
