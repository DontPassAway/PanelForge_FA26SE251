using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Consistency.Models;

/// <summary>
/// Một độ lệch chi tiết do AI phân tích Vision tìm ra (outfit, hair, face, accessory, ...).
/// </summary>
public sealed record ImageConsistencyDeviationDto(
    string Aspect,
    string Description,
    string? RegionHint = null
);

/// <summary>
/// Kết quả kiểm tra tính nhất quán hình ảnh giữa Artwork và Character Sheet đã khóa.
/// </summary>
public sealed record ImageConsistencyResultDto(
    Guid PanelId,
    Guid? CharacterId,
    string? CharacterName,
    int? CharacterSheetVersion,
    decimal Score,
    decimal Threshold,
    bool Passed,
    string? SummaryReason,
    IReadOnlyList<ImageConsistencyDeviationDto> Deviations,
    Guid? FindingId = null,
    string? RevisionRef = null,
    Guid? AssetReferenceId = null,
    string? ArtworkUrl = null,
    string? CharacterSheetUrl = null,
    /// <summary>true = KHÔNG gọi AI (panel chưa có ảnh, không gắn nhân vật, hoặc nhân vật chưa có character sheet); xem SummaryReason.</summary>
    bool Skipped = false
);

/// <summary>
/// Tham số đầu vào để chấm điểm nhất quán hình ảnh giữa 2 ảnh.
/// </summary>
public sealed record ImageConsistencyInput(
    Guid PanelId,
    string? RevisionRef,
    Guid? AssetReferenceId,
    Guid WorkspaceId,
    Guid ChapterId,
    string CharacterName,
    Guid CharacterId,
    int CharacterVersion,
    byte[] CharacterSheetBytes,
    string CharacterSheetMimeType,
    byte[] ArtworkBytes,
    string ArtworkMimeType,
    decimal? Threshold = null,
    Guid? UserId = null
);
