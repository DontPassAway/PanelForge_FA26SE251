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
    NarrationMode DefaultNarrationMode = NarrationMode.Present
);

public sealed record SceneScriptLineSummaryDto(
    Guid Id,
    int LineOrder,
    string? DialogueText,
    string? StageDirection,
    Guid? SpeakerCharacterId,
    Guid? PanelId = null,  // V1.2: Panel mà dòng được neo vào (null = chưa gán)
    IReadOnlyList<Guid>? BibleEntryIds = null,  // UC-06: mục Series Bible dòng nhắc tới (ngoài speaker)
    NarrationMode NarrationMode = NarrationMode.Present
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
    NarrationMode DefaultNarrationMode = NarrationMode.Present
);

public sealed record CreateSceneRequest(
    int? SceneNumber = null,
    string? Heading = null,
    string? Summary = null,
    NarrationMode? DefaultNarrationMode = null
);

public sealed record UpdateSceneRequest(
    string? Heading,
    string? Summary,
    NarrationMode? DefaultNarrationMode = null
);

public sealed record ReorderSceneItem(
    Guid SceneId,
    int NewSceneNumber
);

public sealed record ReorderScenesRequest(
    IReadOnlyList<ReorderSceneItem> Scenes
);
