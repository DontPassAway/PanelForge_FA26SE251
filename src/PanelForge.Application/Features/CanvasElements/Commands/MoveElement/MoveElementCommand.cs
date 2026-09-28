using MediatR;
using PanelForge.Application.Common;

namespace PanelForge.Application.Features.Elements.MoveElement;

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
    long ExpectedPageVersion,   // Optimistic Concurrency: client gửi version đang giữ
    Guid MovedByUserId
) : IRequest<Result<bool>>;
