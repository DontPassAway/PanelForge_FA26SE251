using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Scenes.Models;

public sealed record SceneDto(
    Guid Id,
    Guid ChapterId,
    int SceneNumber,
    string? Heading,
    string? Summary,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    string? CreatedBy,
    int ScriptLinesCount,
    int PagesCount,
    NarrationMode DefaultNarrationMode = NarrationMode.Present,
    int Version = 0   // BR-11: gửi lại làm expectedVersion khi sửa
);

public sealed record SceneScriptLineSummaryDto(
    Guid Id,
    int LineOrder,
    string? DialogueText,
    string? StageDirection,
    Guid? SpeakerCharacterId,
    Guid? PanelId = null,  // V1.2: Panel mà dòng được neo vào (null = chưa gán)
    IReadOnlyList<Guid>? BibleEntryIds = null,  // UC-06: mục Series Bible dòng nhắc tới (ngoài speaker)
    NarrationMode NarrationMode = NarrationMode.Present,
    ScriptLineKind Kind = ScriptLineKind.Action,
    string? SpeakerLabel = null,
    string? Text = null
);

public sealed record SceneDetailDto(
    Guid Id,
    Guid ChapterId,
    int SceneNumber,
    string? Heading,
    string? Summary,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    string? CreatedBy,
    IReadOnlyList<SceneScriptLineSummaryDto> ScriptLines,
    NarrationMode DefaultNarrationMode = NarrationMode.Present,
    int Version = 0   // BR-11: gửi lại làm expectedVersion khi sửa
);

public sealed record CreateSceneRequest(
    int? SceneNumber = null,
    string? Heading = null,
    string? Summary = null,
    NarrationMode? DefaultNarrationMode = null
);

/// <param name="ExpectedVersion">Version của scene lúc client tải (BR-11). Lệch → 409; null = không kiểm (client cũ).</param>
public sealed record UpdateSceneRequest(
    string? Heading,
    string? Summary,
    NarrationMode? DefaultNarrationMode = null,
    int? ExpectedVersion = null
);

public sealed record ReorderSceneItem(
    Guid SceneId,
    int NewSceneNumber
);

public sealed record ReorderScenesRequest(
    IReadOnlyList<ReorderSceneItem> Scenes
);
