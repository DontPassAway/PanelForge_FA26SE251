using PanelForge.Domain.Common;
using PanelForge.Domain.Entities.Workflow;

namespace PanelForge.Domain.Entities.Content;

public class Panel : BaseEntity
{
    public Guid PageId { get; private set; }
    public int PanelNumber { get; private set; }
    public string BoundingBox { get; private set; } = "{}";
    public int ReadingOrder { get; private set; }
    public Guid? CurrentStageId { get; private set; }

    /// <summary>Scene tự sự mà Panel thuộc về (tối đa một, tuỳ chọn). Một Page có thể chứa Panel của nhiều Scene (V1.2).</summary>
    public Guid? SceneId { get; private set; }
    /// <summary>
    /// BR-11 / CF4 4.2: tăng mỗi lần đổi khung, thứ tự đọc hoặc scene; là concurrency token. Client gửi lại version đã thấy
    /// (expectedVersion) để không ghi đè thay đổi của người khác. Riêng từng panel nên sửa panel khác không gây xung đột.
    /// </summary>
    public int Version { get; private set; }

    public Page Page { get; private set; } = default!;
    public Scene? Scene { get; private set; }
    public PipelineStage? CurrentStage { get; private set; }
    public ICollection<ScriptLine> ScriptLines { get; private set; } = [];

    private Panel() { }

    public static Panel Create(
        Guid pageId,
        int panelNumber,
        int readingOrder,
        string? boundingBox = null,
        Guid? sceneId = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(panelNumber);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(readingOrder);

        return new Panel
        {
            PageId = pageId,
            PanelNumber = panelNumber,
            ReadingOrder = readingOrder,
            BoundingBox = boundingBox?.Trim() ?? "{}",
            SceneId = sceneId
        };
    }

    public void AssignToScene(Guid? sceneId)
    {
        SceneId = sceneId;
        Version++;
    }

    public void UpdateBoundingBox(string boundingBoxJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(boundingBoxJson);
        BoundingBox = boundingBoxJson;
        Version++;
    }

    public void UpdateReadingOrder(int readingOrder)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(readingOrder);
        ReadingOrder = readingOrder;
        Version++;
    }

    public void AdvanceToStage(Guid? stageId) => CurrentStageId = stageId;
}
