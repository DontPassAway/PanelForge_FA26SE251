using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PanelForge.API.Common.Attributes;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Exports.Commands;
using PanelForge.Application.Features.Exports.Models;
using PanelForge.Application.Features.Exports.Queries;
using PanelForge.Domain.Enums;

namespace PanelForge.API.Controllers;

/// <summary>
/// CF5 UC-12 / UC-14: xuất chương đã duyệt (CBZ, PDF, ảnh trang, webtoon) chạy nền. Producer tạo và tải; file chỉ đi qua API
/// có kiểm quyền Workspace, không lộ đường dẫn kho (BR-16).
/// </summary>
[ApiController]
[Authorize]
[Route("api/chapters/{chapterId:guid}/exports")]
public sealed class ExportsController : ControllerBase
{
    private readonly IMediator _mediator;

    public ExportsController(IMediator mediator) => _mediator = mediator;

    private Guid GetCurrentUserId() =>
        Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : Guid.Empty;

    /// <summary>POST /api/chapters/{chapterId}/exports — body { presetCode }. 202: job đã xếp hàng.</summary>
    [HttpPost]
    [RequireWorkspaceRole(WorkspaceRole.Producer)]
    [ProducesResponseType(typeof(ExportJobDto), StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Create(Guid chapterId, [FromBody] CreateExportJobRequest body, CancellationToken ct)
    {
        var result = await _mediator.Send(new CreateExportJobCommand(chapterId, GetCurrentUserId(), body.PresetCode), ct);
        return result.IsSuccess ? Accepted(result.Value) : Error(result);
    }

    /// <summary>GET /api/chapters/{chapterId}/exports — các bản xuất, mới nhất trước.</summary>
    [HttpGet]
    [RequireWorkspaceRole(WorkspaceRole.Producer, WorkspaceRole.Editor, WorkspaceRole.Publisher)]
    [ProducesResponseType(typeof(IReadOnlyList<ExportJobDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(Guid chapterId, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetExportJobsQuery(chapterId), ct);
        return result.IsSuccess ? Ok(result.Value) : Error(result);
    }

    /// <summary>POST /api/chapters/{chapterId}/exports/{jobId}/cancel — chỉ job còn Queued.</summary>
    [HttpPost("{jobId:guid}/cancel")]
    [RequireWorkspaceRole(WorkspaceRole.Producer)]
    [ProducesResponseType(typeof(ExportJobDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Cancel(Guid chapterId, Guid jobId, CancellationToken ct)
    {
        var result = await _mediator.Send(new CancelExportJobCommand(chapterId, jobId, GetCurrentUserId()), ct);
        return result.IsSuccess ? Ok(result.Value) : Error(result);
    }

    /// <summary>GET /api/chapters/{chapterId}/exports/{jobId}/file — tải file đã xuất (mỗi lần tải được ghi lại).</summary>
    [HttpGet("{jobId:guid}/file")]
    [RequireWorkspaceRole(WorkspaceRole.Producer)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Download(Guid chapterId, Guid jobId, CancellationToken ct)
    {
        var result = await _mediator.Send(new DownloadExportFileCommand(chapterId, jobId, GetCurrentUserId()), ct);
        if (!result.IsSuccess) return Error(result);
        Response.Headers.CacheControl = "no-store";
        return File(result.Value!.Content, result.Value.ContentType, result.Value.FileName);
    }

    private IActionResult Error<T>(Result<T> result) => result.ErrorCode switch
    {
        ResultErrorCodes.NotFound => NotFound(new { error = result.ErrorMessage }),
        ResultErrorCodes.Conflict => Conflict(new { error = result.ErrorMessage, code = result.ErrorCode }),
        _ => BadRequest(new { error = result.ErrorMessage, code = result.ErrorCode })
    };
}
