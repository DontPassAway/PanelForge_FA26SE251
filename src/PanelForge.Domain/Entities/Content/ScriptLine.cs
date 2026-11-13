using PanelForge.Domain.Common;
using PanelForge.Domain.Entities.Bible;
using PanelForge.Domain.Enums;

namespace PanelForge.Domain.Entities.Content;

public class ScriptLine : BaseEntity
{
    public Guid SceneId { get; private set; }
    public int LineOrder { get; private set; }
    public ScriptLineKind Kind { get; private set; } = ScriptLineKind.Action;
    public string? Text { get; private set; }
    public string? SpeakerLabel { get; private set; }
    public Guid? SpeakerCharacterId { get; private set; }

    // Tương thích ngược: chỉ giữ để code cũ biên dịch tới khi chuyển xong, xoá ở SCR-23
    public string? DialogueText { get; private set; }
    public string? StageDirection { get; private set; }
    public NarrationMode NarrationMode { get; private set; } = NarrationMode.Present;

    /// <summary>Panel mà dòng được neo vào sau breakdown (tối đa một). Phân trang lại chỉ đổi giá trị này, dòng giữ nguyên.</summary>
    public Guid? PanelId { get; private set; }

    public Scene Scene { get; private set; } = default!;
    public Panel? Panel { get; private set; }
    public BibleEntry? SpeakerCharacter { get; private set; }
    public ICollection<ScriptLineBibleLink> BibleLinks { get; private set; } = [];

    private ScriptLine() { }

    public static ScriptLine Create(
        Guid sceneId,
        int lineOrder,
        string? dialogueText = null,
        string? stageDirection = null,
        Guid? speakerCharacterId = null,
        Guid? id = null,
        NarrationMode narrationMode = NarrationMode.Present,
        ScriptLineKind? kind = null,
        string? speakerLabel = null,
        string? text = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(lineOrder);

        var resolvedText = text?.Trim() ?? (!string.IsNullOrWhiteSpace(dialogueText) ? dialogueText.Trim() : stageDirection?.Trim());
        if (string.IsNullOrWhiteSpace(resolvedText))
            throw new ArgumentException("ScriptLine phải có nội dung văn bản (Text, DialogueText hoặc StageDirection).");

        var resolvedKind = kind ?? (!string.IsNullOrWhiteSpace(dialogueText) || speakerCharacterId.HasValue
            ? ScriptLineKind.Dialogue
            : ScriptLineKind.Action);

        var trimmedSpeakerLabel = speakerLabel?.Trim();
        if (trimmedSpeakerLabel?.Length > 100)
            throw new ArgumentException("SpeakerLabel không được vượt quá 100 ký tự.");

        // SCR-04 Luật domain: Action không có speaker
        if (resolvedKind == ScriptLineKind.Action && (speakerCharacterId.HasValue || !string.IsNullOrWhiteSpace(trimmedSpeakerLabel)))
            throw new InvalidOperationException("Dòng Action không được có người nói (SpeakerCharacterId hoặc SpeakerLabel).");

        return new ScriptLine
        {
            Id = id ?? Guid.NewGuid(),
            SceneId = sceneId,
            LineOrder = lineOrder,
            Kind = resolvedKind,
            Text = resolvedText,
            SpeakerLabel = trimmedSpeakerLabel,
            SpeakerCharacterId = resolvedKind == ScriptLineKind.Action ? null : speakerCharacterId,
            DialogueText = resolvedKind == ScriptLineKind.Dialogue || resolvedKind == ScriptLineKind.Thought ? resolvedText : dialogueText?.Trim(),
            StageDirection = resolvedKind != ScriptLineKind.Dialogue && resolvedKind != ScriptLineKind.Thought ? resolvedText : stageDirection?.Trim(),
            NarrationMode = narrationMode
        };
    }

    public void UpdateContent(
        string? dialogueText,
        string? stageDirection,
        Guid? speakerCharacterId,
        NarrationMode narrationMode = NarrationMode.Present,
        ScriptLineKind? kind = null,
        string? speakerLabel = null,
        string? text = null)
    {
        var resolvedText = text?.Trim() ?? (!string.IsNullOrWhiteSpace(dialogueText) ? dialogueText.Trim() : stageDirection?.Trim());
        if (string.IsNullOrWhiteSpace(resolvedText))
            throw new ArgumentException("ScriptLine phải có nội dung văn bản.");

        var resolvedKind = kind ?? Kind;
        var trimmedSpeakerLabel = speakerLabel?.Trim();
        if (trimmedSpeakerLabel?.Length > 100)
            throw new ArgumentException("SpeakerLabel không được vượt quá 100 ký tự.");

        if (resolvedKind == ScriptLineKind.Action && (speakerCharacterId.HasValue || !string.IsNullOrWhiteSpace(trimmedSpeakerLabel)))
            throw new InvalidOperationException("Dòng Action không được có người nói.");

        Kind = resolvedKind;
        Text = resolvedText;
        SpeakerLabel = trimmedSpeakerLabel;
        SpeakerCharacterId = resolvedKind == ScriptLineKind.Action ? null : speakerCharacterId;
        DialogueText = resolvedKind == ScriptLineKind.Dialogue || resolvedKind == ScriptLineKind.Thought ? resolvedText : dialogueText?.Trim();
        StageDirection = resolvedKind != ScriptLineKind.Dialogue && resolvedKind != ScriptLineKind.Thought ? resolvedText : stageDirection?.Trim();
        NarrationMode = narrationMode;
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdateNarrationMode(NarrationMode narrationMode) => NarrationMode = narrationMode;

    public void AssignToPanel(Guid? panelId) => PanelId = panelId;

    public void Reorder(int newOrder)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(newOrder);
        LineOrder = newOrder;
    }
}
