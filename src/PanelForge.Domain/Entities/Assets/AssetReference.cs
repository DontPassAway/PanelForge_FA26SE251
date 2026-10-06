using PanelForge.Domain.Common;

namespace PanelForge.Domain.Entities.Assets;

/// <summary>
/// Một lần dùng Asset làm artwork của một Page ở một Stage (BR-13). Đây cũng là phiên bản artwork: chỉ thêm, không sửa
/// hay xoá, nên lịch sử các lần tải lên được giữ nguyên (BR-01). <see cref="VersionNumber"/> tăng dần theo (Page, Stage).
/// </summary>
public class AssetReference : BaseEntity
{
    public Guid AssetId { get; private set; }
    public Guid WorkspaceId { get; private set; }
    public Guid ChapterId { get; private set; }
    public Guid PageId { get; private set; }
    public Guid PipelineStageId { get; private set; }
    public int VersionNumber { get; private set; }
    /// <summary>Mã loại Element mà Artist khai báo cho layer này (vd ARTWORK_LAYER); null = không khai báo.</summary>
    public string? LayerType { get; private set; }
    public string FileName { get; private set; } = default!;
    public Guid UploadedByUserId { get; private set; }

    private AssetReference() { }

    public static AssetReference Create(
        Guid assetId, Guid workspaceId, Guid chapterId, Guid pageId, Guid pipelineStageId,
        int versionNumber, string? layerType, string fileName, Guid uploadedByUserId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(versionNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        return new AssetReference
        {
            AssetId = assetId,
            WorkspaceId = workspaceId,
            ChapterId = chapterId,
            PageId = pageId,
            PipelineStageId = pipelineStageId,
            VersionNumber = versionNumber,
            LayerType = string.IsNullOrWhiteSpace(layerType) ? null : layerType.Trim(),
            FileName = fileName.Trim(),
            UploadedByUserId = uploadedByUserId,
            CreatedAt = DateTime.UtcNow
        };
    }
}
