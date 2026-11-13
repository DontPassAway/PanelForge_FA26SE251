namespace PanelForge.Application.Features.Panels.Models;

public sealed record PanelDto(
    Guid Id,
    Guid PageId,
    int PanelNumber,
    string BoundingBox,
    int ReadingOrder,
    Guid? CurrentStageId,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    string? CreatedBy,
    Guid? SceneId = null,
    int Version = 0,   // BR-11: gửi lại làm expectedVersion khi sửa
    string? Direction = null
);

public sealed record PanelDetailDto(
    Guid Id,
    Guid PageId,
    int PanelNumber,
    string BoundingBox,
    int ReadingOrder,
    Guid? CurrentStageId,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    string? CreatedBy,
    Guid? SceneId = null,
    IReadOnlyList<Guid>? ScriptLineIds = null,   // dòng thoại đang neo vào panel này (V1.2)
    int Version = 0,
    string? Direction = null
);

public sealed record AssignPanelSceneRequest(Guid? SceneId);

public sealed record AssignPanelScriptLinesRequest(IReadOnlyList<Guid> ScriptLineIds);

public sealed record AutoAssignScriptLinesRequest(Guid SceneId, bool DryRun = false);

public sealed record CreatePanelRequest(
    int? PanelNumber = null,
    int? ReadingOrder = null,
    string? BoundingBox = null,
    string? Direction = null
);

/// <param name="ExpectedVersion">Version của panel lúc client tải (BR-11). Lệch → 409; null = không kiểm (client cũ).</param>
public sealed record UpdatePanelCoordinatesRequest(
    string BoundingBox,
    int? ReadingOrder = null,
    int? ExpectedVersion = null
);

public sealed record UpdatePanelDirectionRequest(
    string? Direction,
    int? ExpectedVersion = null
);
