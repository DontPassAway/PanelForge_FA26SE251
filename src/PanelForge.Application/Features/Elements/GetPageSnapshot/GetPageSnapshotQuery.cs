using MediatR;
using PanelForge.Application.Common;

namespace PanelForge.Application.Features.Elements.GetPageSnapshot;

/// <summary>
/// Query: Lấy toàn bộ trạng thái hiện tại của Page và các Elements.
/// Read side của CQRS — rebuild từ aggregate sau khi replay event stream.
/// </summary>
public sealed record GetPageSnapshotQuery(
    Guid PageId,
    bool IncludeRemoved = false  // true = trả về cả elements đã bị soft-delete
) : IRequest<Result<PageSnapshotDto>>;

// ── DTOs ──────────────────────────────────────────────────────────────────────

public sealed record PageSnapshotDto(
    Guid PageId,
    long Version,
    int PageNumber,
    IReadOnlyList<ElementSnapshotDto> Elements
);

public sealed record ElementSnapshotDto(
    Guid ElementId,
    string ElementType,
    int ZIndex,
    double X,
    double Y,
    double Width,
    double Height,
    string? Content,
    string? AssetId,
    bool IsRemoved
);
