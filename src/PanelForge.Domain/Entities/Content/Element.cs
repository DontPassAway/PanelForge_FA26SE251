using PanelForge.Domain.Common;
using PanelForge.Domain.Enums;

namespace PanelForge.Domain.Entities.Content;

public class Element : BaseEntity
{
    public Guid PanelId { get; private set; }
    public ElementType ElementType { get; private set; }
    public Guid? ScriptLineId { get; private set; }
    public Guid? SpeakerCharacterId { get; private set; }
    public int ZIndex { get; private set; }
    public string TransformGeometry { get; private set; } = "{}";
    public string? Content { get; private set; }
    public string StyleProperties { get; private set; } = "{}";

    public Panel Panel { get; private set; } = default!;
    public ScriptLine? ScriptLine { get; private set; }

    private Element() { }

    public static Element Create(
        Guid panelId,
        ElementType elementType,
        int zIndex,
        string? content = null,
        string? transformGeometry = null,
        string? styleProperties = null,
        Guid? scriptLineId = null,
        Guid? speakerCharacterId = null)
    {
        return new Element
        {
            PanelId = panelId,
            ElementType = elementType,
            ZIndex = zIndex,
            Content = content?.Trim(),
            TransformGeometry = transformGeometry?.Trim() ?? "{}",
            StyleProperties = styleProperties?.Trim() ?? "{}",
            ScriptLineId = scriptLineId,
            SpeakerCharacterId = speakerCharacterId
        };
    }

    public void UpdateContent(string? content) => Content = content?.Trim();

    public void UpdateTransform(string transformGeometry)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(transformGeometry);
        TransformGeometry = transformGeometry;
    }

    public void UpdateStyle(string styleProperties)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(styleProperties);
        StyleProperties = styleProperties;
    }

    public void UpdateZIndex(int zIndex) => ZIndex = zIndex;

    public void BindToScriptLine(Guid? scriptLineId) => ScriptLineId = scriptLineId;

    public void AssignSpeaker(Guid? characterId) => SpeakerCharacterId = characterId;
}
