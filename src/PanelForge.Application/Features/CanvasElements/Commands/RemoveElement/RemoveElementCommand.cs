using MediatR;
using PanelForge.Application.Common;

namespace PanelForge.Application.Features.Elements.RemoveElement;

/// <summary>
/// Command: Xóa mềm (Soft Delete) một Element.
/// Thay vì DELETE row, hệ thống APPEND một ElementRemovedEvent vào Event Store.
/// Element vẫn tồn tại trong lịch sử và có thể Restore bất kỳ lúc nào.
/// </summary>
public sealed record RemoveElementCommand(
    Guid PageId,
    Guid ElementId,
    string Reason,               // "USER_DELETE" | "REPLACED" | "OUT_OF_FRAME"
    Guid RemovedByUserId
) : IRequest<Result<bool>>;
