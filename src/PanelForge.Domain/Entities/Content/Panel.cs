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

    public Page Page { get; private set; } = default!;
    public PipelineStage? CurrentStage { get; private set; }
    public ICollection<Element> Elements { get; private set; } = [];

    private Panel() { }

    public static Panel Create(
        Guid pageId,
        int panelNumber,
        int readingOrder,
        string? boundingBox = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(panelNumber);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(readingOrder);

        return new Panel
        {
            PageId = pageId,
            PanelNumber = panelNumber,
            ReadingOrder = readingOrder,
            BoundingBox = boundingBox?.Trim() ?? "{}"
        };
    }

    public void UpdateBoundingBox(string boundingBoxJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(boundingBoxJson);
        BoundingBox = boundingBoxJson;
    }

    public void UpdateReadingOrder(int readingOrder)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(readingOrder);
        ReadingOrder = readingOrder;
    }

    public void AdvanceToStage(Guid? stageId) => CurrentStageId = stageId;
}
