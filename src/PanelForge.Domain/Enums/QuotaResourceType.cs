namespace PanelForge.Domain.Enums;

/// <summary>Loại tài nguyên Admin cấp cho Studio (OPS-20, OPS-21). Lưu dạng số; không đổi số cũ.</summary>
public enum QuotaResourceType
{
    /// <summary>Dung lượng lưu trữ (byte), cột <c>studio_workspaces.storage_quota_bytes</c>.</summary>
    Storage = 1,
    /// <summary>Token AI mỗi tháng, cột <c>workspace_ai_configs.monthly_token_quota</c>.</summary>
    AiTokens = 2,
    /// <summary>Ảnh AI mỗi tháng, cột <c>workspace_ai_configs.monthly_image_quota</c>.</summary>
    AiImages = 3
}
