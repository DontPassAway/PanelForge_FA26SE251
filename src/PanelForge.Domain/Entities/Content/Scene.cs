using PanelForge.Domain.Common;

namespace PanelForge.Domain.Entities.Content;

public class Scene : BaseEntity
{
    public Guid ChapterId { get; private set; }
    public int SceneNumber { get; private set; }
    public string? Heading { get; private set; }
    public string? Summary { get; private set; }

    public Chapter Chapter { get; private set; } = default!;
    public ICollection<ScriptLine> ScriptLines { get; private set; } = [];
    public ICollection<Page> Pages { get; private set; } = [];

    private Scene() { }

    public static Scene Create(Guid chapterId, int sceneNumber, string? heading = null, string? summary = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sceneNumber);

        return new Scene
        {
            ChapterId = chapterId,
            SceneNumber = sceneNumber,
            Heading = heading?.Trim(),
            Summary = summary?.Trim()
        };
    }

    public void UpdateContent(string? heading, string? summary)
    {
        Heading = heading?.Trim();
        Summary = summary?.Trim();
    }

    public void Reorder(int newSceneNumber)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(newSceneNumber);
        SceneNumber = newSceneNumber;
    }
}
