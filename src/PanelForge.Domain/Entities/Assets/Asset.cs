using PanelForge.Domain.Common;

namespace PanelForge.Domain.Entities.Assets;

/// <summary>
/// Nội dung file nhận diện theo SHA-256 trong một Workspace (BR-04, BR-13). Hai lần tải cùng nội dung dùng chung một Asset,
/// chỉ tạo thêm <see cref="AssetReference"/>. Bất biến sau khi tạo.
/// </summary>
public class Asset : BaseEntity
{
    public Guid WorkspaceId { get; private set; }
    /// <summary>SHA-256 do server tính, 64 ký tự hex thường.</summary>
    public string Sha256 { get; private set; } = default!;
    public long SizeBytes { get; private set; }
    public string ContentType { get; private set; } = default!;
    /// <summary>Khoá trong kho lưu trữ, sinh theo nội dung nên không đoán được từ tên file.</summary>
    public string StorageKey { get; private set; } = default!;

    private Asset() { }

    public static Asset Create(Guid workspaceId, string sha256, long sizeBytes, string contentType)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sizeBytes);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);
        if (sha256 is not { Length: 64 } || !sha256.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f'))
            throw new ArgumentException("SHA-256 phải gồm 64 ký tự hex thường.", nameof(sha256));

        return new Asset
        {
            WorkspaceId = workspaceId,
            Sha256 = sha256,
            SizeBytes = sizeBytes,
            ContentType = contentType.Trim(),
            StorageKey = $"{workspaceId:N}/{sha256[..2]}/{sha256}",
            CreatedAt = DateTime.UtcNow
        };
    }
}
