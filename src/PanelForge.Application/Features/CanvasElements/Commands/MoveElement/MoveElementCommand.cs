using MediatR;
using PanelForge.Application.Common;

namespace PanelForge.Application.Features.CanvasElements.Commands.MoveElement;

/// <summary>
/// Command: Di chuyển / Resize một Element.
/// Thay vì UPDATE row, hệ thống APPEND một ElementMovedEvent vào Event Store.
/// Lịch sử vị trí cũ được bảo toàn hoàn toàn.
/// </summary>
public sealed record MoveElementCommand(
    Guid PageId,
    Guid ElementId,
    double NewX,
    double NewY,
    double NewWidth,
    double NewHeight,
    long? ExpectedPageVersion,  // Cách cũ: version cả Page (xung đột cả khi người khác sửa element KHÁC)
    Guid MovedByUserId,
    int? ExpectedElementVersion = null,  // BR-11: version của element đang giữ; ưu tiên hơn ExpectedPageVersion
    double? NewRotation = null,          // null = giữ nguyên góc xoay
    int? NewZIndex = null                // null = giữ nguyên thứ tự lớp
) : IRequest<Result<bool>>;
