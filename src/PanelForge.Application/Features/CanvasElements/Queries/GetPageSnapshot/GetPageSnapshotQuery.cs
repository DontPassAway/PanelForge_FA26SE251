using MediatR;
using PanelForge.Application.Common;
using PanelForge.Application.Features.CanvasElements.Common;

namespace PanelForge.Application.Features.CanvasElements.Queries.GetPageSnapshot;

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
    IReadOnlyList<ElementSnapshotDto> Elements,
    IReadOnlyList<ElementWarningDto>? Warnings = null  // BR-20: cảnh báo bằng code, không chặn nộp
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
    bool IsRemoved,
    Guid? ScriptLineId = null,
    Guid? SpeakerId = null,
    string? RenderedText = null,
    string? CurrentLineText = null,  // thoại hiện tại của ScriptLine (null nếu không gắn / dòng đã xóa)
    bool IsStale = false,            // true khi Writer đã sửa thoại/speaker hoặc xóa dòng sau lúc gắn
    int? ReadingOrder = null,        // thứ tự đọc Letterer ghi đè (null = suy từ vị trí)
    string? OverflowAcknowledgedReason = null  // lý do xác nhận cảnh báo tràn chữ (BR-20), null = chưa xác nhận
);
