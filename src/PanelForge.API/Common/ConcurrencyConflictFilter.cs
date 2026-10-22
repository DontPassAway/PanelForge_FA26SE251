using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;

namespace PanelForge.API.Common;

/// <summary>
/// Hai thao tác cùng lúc trên một bản ghi có concurrency token (vd chuyển giao và duyệt cùng một Assignment): lần lưu sau
/// không được ghi đè lên dữ liệu vừa đổi. Trả 409 cùng dạng lỗi với controller để client tải lại rồi thử lại.
/// </summary>
public sealed class ConcurrencyConflictFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is not DbUpdateConcurrencyException) return;

        context.Result = new ConflictObjectResult(new
        {
            error = "Dữ liệu vừa được người khác thay đổi. Hãy tải lại rồi thử lại.",
            code = ResultErrorCodes.Conflict
        });
        context.ExceptionHandled = true;
    }
}
