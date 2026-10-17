using PanelForge.Domain.Common;
using PanelForge.Domain.Enums;

namespace PanelForge.Domain.Entities.Content;

public class Scene : BaseEntity
{
    public Guid ChapterId { get; private set; }
    public int SceneNumber { get; private set; }
    public string? Heading { get; private set; }
    public string? Summary { get; private set; }
    public NarrationMode DefaultNarrationMode { get; private set; } = NarrationMode.Present;
    /// <summary>
    /// BR-11 / CF4 4.2: tăng mỗi lần đổi nội dung (heading, tóm tắt, narration); là concurrency token. Đổi số thứ tự cảnh
    /// không tăng để sắp xếp lại không làm hỏng lần sửa nội dung đang dở của người khác.
    /// </summary>
    public int Version { get; private set; }

    public Chapter Chapter { get; private set; } = default!;
    public ICollection<ScriptLine> ScriptLines { get; private set; } = [];
    /// <summary>Scene không còn chứa Page (V1.2): chỉ Panel mang SceneId.</summary>
    public ICollection<Panel> Panels { get; private set; } = [];

    private Scene() { }

    public static Scene Create(
        Guid chapterId, int sceneNumber, string? heading = null, string? summary = null,
        NarrationMode defaultNarrationMode = NarrationMode.Present)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sceneNumber);

        return new Scene
        {
            ChapterId = chapterId,
            SceneNumber = sceneNumber,
            Heading = heading?.Trim(),
            Summary = summary?.Trim(),
            DefaultNarrationMode = defaultNarrationMode
        };
    }

    public void UpdateContent(string? heading, string? summary, NarrationMode? defaultNarrationMode = null)
    {
        Heading = heading?.Trim();
        Summary = summary?.Trim();
        if (defaultNarrationMode.HasValue)
        {
            DefaultNarrationMode = defaultNarrationMode.Value;
        }
        Version++;
    }

    public void Reorder(int newSceneNumber)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(newSceneNumber);
        SceneNumber = newSceneNumber;
    }
}
