using PanelForge.Domain.Common;
using PanelForge.Domain.Entities.Bible;

namespace PanelForge.Domain.Entities.Content;

public class ScriptLine : BaseEntity
{
    public Guid SceneId { get; private set; }
    public int LineOrder { get; private set; }
    public Guid? SpeakerCharacterId { get; private set; }
    public string? DialogueText { get; private set; }
    public string? StageDirection { get; private set; }

    public Scene Scene { get; private set; } = default!;
    public BibleEntry? SpeakerCharacter { get; private set; }

    private ScriptLine() { }

    public static ScriptLine Create(
        Guid sceneId,
        int lineOrder,
        string? dialogueText = null,
        string? stageDirection = null,
        Guid? speakerCharacterId = null,
        Guid? id = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(lineOrder);

        if (string.IsNullOrWhiteSpace(dialogueText) && string.IsNullOrWhiteSpace(stageDirection))
            throw new ArgumentException("ScriptLine phải có ít nhất DialogueText hoặc StageDirection.");

        return new ScriptLine
        {
            // Id do ứng dụng cấp (AI draft) để sourceScriptLineIds của Storyboard trỏ đúng dòng sau khi Accept
            Id = id ?? Guid.NewGuid(),
            SceneId = sceneId,
            LineOrder = lineOrder,
            DialogueText = dialogueText?.Trim(),
            StageDirection = stageDirection?.Trim(),
            SpeakerCharacterId = speakerCharacterId
        };
    }

    public void UpdateContent(string? dialogueText, string? stageDirection, Guid? speakerCharacterId)
    {
        DialogueText = dialogueText?.Trim();
        StageDirection = stageDirection?.Trim();
        SpeakerCharacterId = speakerCharacterId;
    }

    public void Reorder(int newOrder)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(newOrder);
        LineOrder = newOrder;
    }
}
