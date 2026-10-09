using FirebaseAdmin.Auth;
using PanelForge.Application.Common.Interfaces.Authentication;

namespace PanelForge.Infrastructure.ExternalServices.Firebase;

/// <summary>Verify Firebase ID Token. Mọi exception của Firebase SDK được chuyển thành ExternalIdentityResult.</summary>
public sealed class FirebaseIdentityVerifier : IExternalIdentityVerifier
{
    public async Task<ExternalIdentityResult> VerifyAsync(string idToken, CancellationToken cancellationToken = default)
    {
        var auth = FirebaseAuth.DefaultInstance;
        if (auth is null)
            return new ExternalIdentityResult(
                ExternalIdentityStatus.NotConfigured,
                ErrorMessage: "FirebaseApp chưa được khởi tạo. Vui lòng cấu hình Firebase trong appsettings.json.");

        FirebaseToken decoded;
        try
        {
            decoded = await auth.VerifyIdTokenAsync(idToken, cancellationToken);
        }
        catch (FirebaseAuthException ex)
        {
            return new ExternalIdentityResult(
                ExternalIdentityStatus.InvalidToken,
                ErrorMessage: $"Xác thực Firebase ID Token thất bại: {ex.Message}");
        }

        return new ExternalIdentityResult(
            ExternalIdentityStatus.Valid,
            Uid: decoded.Uid,
            Email: ReadClaim(decoded, "email"),
            Name: ReadClaim(decoded, "name"),
            PictureUrl: ReadClaim(decoded, "picture"));
    }

    private static string? ReadClaim(FirebaseToken token, string key)
        => token.Claims.TryGetValue(key, out var value) ? value?.ToString() : null;
}
