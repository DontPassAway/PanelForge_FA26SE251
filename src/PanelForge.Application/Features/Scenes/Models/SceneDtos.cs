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
    int PagesCount
);

public sealed record SceneScriptLineSummaryDto(
    Guid Id,
    int LineOrder,
    string? DialogueText,
    string? StageDirection,
    Guid? SpeakerCharacterId
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
    IReadOnlyList<SceneScriptLineSummaryDto> ScriptLines
);

public sealed record CreateSceneRequest(
    int? SceneNumber = null,
    string? Heading = null,
    string? Summary = null
);

public sealed record UpdateSceneRequest(
    string? Heading,
    string? Summary
);

public sealed record ReorderSceneItem(
    Guid SceneId,
    int NewSceneNumber
);

public sealed record ReorderScenesRequest(
    IReadOnlyList<ReorderSceneItem> Scenes
);
