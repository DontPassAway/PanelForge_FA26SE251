using PanelForge.Domain.Common;
using PanelForge.Domain.Enums;

namespace PanelForge.Domain.Entities.Content;

public class Page : BaseEntity
{
    public Guid ChapterId { get; private set; }
    public Guid? SceneId { get; private set; }
    public int PageNumber { get; private set; }
    public LayoutFormat LayoutFormat { get; private set; }
    public int WidthPx { get; private set; }
    public int HeightPx { get; private set; }
    public int Dpi { get; private set; }

    public Chapter Chapter { get; private set; } = default!;
    public Scene? Scene { get; private set; }
    public ICollection<Panel> Panels { get; private set; } = [];

    private Page() { }

    public static Page Create(
        Guid chapterId,
        int pageNumber,
        LayoutFormat layoutFormat,
        int widthPx,
        int heightPx,
        int dpi,
        Guid? sceneId = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageNumber);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(widthPx);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(heightPx);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dpi);

        return new Page
        {
            ChapterId = chapterId,
            SceneId = sceneId,
            PageNumber = pageNumber,
            LayoutFormat = layoutFormat,
            WidthPx = widthPx,
            HeightPx = heightPx,
            Dpi = dpi
        };
    }

    public void UpdateCanvasSettings(int widthPx, int heightPx, int dpi, LayoutFormat layoutFormat)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(widthPx);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(heightPx);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dpi);
        WidthPx = widthPx;
        HeightPx = heightPx;
        Dpi = dpi;
        LayoutFormat = layoutFormat;
    }

    public void AssignToScene(Guid? sceneId) => SceneId = sceneId;

    public void Reorder(int newPageNumber)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(newPageNumber);
        PageNumber = newPageNumber;
    }
}
