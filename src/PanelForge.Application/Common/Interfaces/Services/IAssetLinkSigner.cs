namespace PanelForge.Application.Common.Interfaces.Services;

/// <summary>Nội dung đã xác thực của một link tải artwork.</summary>
public sealed record AssetLinkClaims(Guid AssetReferenceId, Guid UserId);

/// <summary>
/// Ký và kiểm link tải artwork ngắn hạn (BR-16). Là port vì đi qua ranh giới bảo mật (khoá ký do hạ tầng giữ),
/// handler không tự xử lý mật mã.
/// </summary>
public interface IAssetLinkSigner
{
    /// <summary>Tạo token gắn với một artwork và một người dùng, tự hết hạn sau <paramref name="lifetime"/>.</summary>
    string Create(Guid assetReferenceId, Guid userId, TimeSpan lifetime);

    /// <summary>Trả claims nếu token đúng chữ ký và chưa hết hạn; null nếu sai, bị sửa hoặc hết hạn.</summary>
    AssetLinkClaims? Validate(string token);
}
