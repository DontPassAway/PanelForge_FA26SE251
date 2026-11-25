using Microsoft.AspNetCore.Mvc;
using PanelForge.Application.Common;

namespace PanelForge.API.Common;

/// <summary>
/// Cho các controller còn đoán mã lỗi theo nội dung message: lỗi Conflict / ChapterReadOnly (BR-12) thành 409 kèm
/// <c>code</c>; null để controller xử lý như cũ.
/// </summary>
public static class ConflictResultExtensions
{
    public static IActionResult? ConflictOrNull<T>(this ControllerBase controller, Result<T> result) => result.ErrorCode switch
    {
        ResultErrorCodes.Conflict or ResultErrorCodes.ChapterReadOnly =>
            controller.Conflict(new { error = result.ErrorMessage, code = result.ErrorCode }),
        // Ngoài phạm vi việc được giao (WorkScopeGuard)
        ResultErrorCodes.Forbidden =>
            controller.StatusCode(StatusCodes.Status403Forbidden, new { error = result.ErrorMessage, code = result.ErrorCode }),
        _ => null
    };
}
