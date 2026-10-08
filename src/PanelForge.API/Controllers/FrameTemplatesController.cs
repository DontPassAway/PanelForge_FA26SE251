using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PanelForge.API.Common.Attributes;
using PanelForge.Application.Common;
using PanelForge.Application.Features.FrameTemplates.Commands.ApplyFrameTemplate;
using PanelForge.Application.Features.FrameTemplates.Commands.CreateFrameTemplate;
using PanelForge.Application.Features.FrameTemplates.Commands.DeleteFrameTemplate;
using PanelForge.Application.Features.FrameTemplates.Models;
using PanelForge.Application.Features.FrameTemplates.Queries.GetFrameTemplates;
using PanelForge.Application.Features.Panels.Models;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;

namespace PanelForge.API.Controllers;

/// <summary>Frame template: Artist chọn khung dựng sẵn để dựng Page (CF2 bước 9), lưu bố cục thành template của Workspace.</summary>
[ApiController]
[Authorize]
public sealed class FrameTemplatesController : ControllerBase
{
    private readonly IMediator _mediator;

    public FrameTemplatesController(IMediator mediator) => _mediator = mediator;

    /// <summary>GET /api/workspaces/{workspaceId}/frame-templates — template có sẵn + template của Workspace (mọi thành viên).</summary>
    [HttpGet("api/workspaces/{workspaceId:guid}/frame-templates")]
    [RequireWorkspaceRole]
    [ProducesResponseType(typeof(IReadOnlyList<FrameTemplateDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(Guid workspaceId, CancellationToken ct) =>
        Ok(await _mediator.Send(new GetFrameTemplatesQuery(workspaceId), ct));

    /// <summary>POST /api/workspaces/{workspaceId}/frame-templates — lưu bố cục thành template của Workspace.</summary>
    [HttpPost("api/workspaces/{workspaceId:guid}/frame-templates")]
    [RequireWorkspaceRole(WorkspaceRole.Producer, WorkspaceRole.Artist)]
    [ProducesResponseType(typeof(FrameTemplateDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(Guid workspaceId, [FromBody] CreateFrameTemplateRequest body, CancellationToken ct)
    {
        var result = await _mediator.Send(new CreateFrameTemplateCommand(
            workspaceId, GetCurrentUserId(), body.Name, body.LayoutFormat ?? LayoutFormat.StandardPage, body.Slots ?? []), ct);
        return result.IsSuccess ? StatusCode(StatusCodes.Status201Created, result.Value) : Failure(result.ErrorMessage, result.ErrorCode);
    }

    /// <summary>DELETE /api/workspaces/{workspaceId}/frame-templates/{templateId} — xoá mềm template của Workspace (Producer).</summary>
    [HttpDelete("api/workspaces/{workspaceId:guid}/frame-templates/{templateId:guid}")]
    [RequireWorkspaceRole(WorkspaceRole.Producer)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid workspaceId, Guid templateId, CancellationToken ct)
    {
        var result = await _mediator.Send(new DeleteFrameTemplateCommand(workspaceId, templateId), ct);
        return result.IsSuccess ? NoContent() : Failure(result.ErrorMessage, result.ErrorCode);
    }

    /// <summary>POST /api/pages/{pageId}/frame-template — dựng Page chưa có Panel từ template. Body: { "templateId": "guid" }</summary>
    [HttpPost("api/pages/{pageId:guid}/frame-template")]
    [RequireWorkspaceRole(WorkspaceRole.Producer, WorkspaceRole.Artist)]
    [ProducesResponseType(typeof(IReadOnlyList<PanelDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Apply(Guid pageId, [FromBody] ApplyFrameTemplateRequest body, CancellationToken ct)
    {
        var result = await _mediator.Send(new ApplyFrameTemplateCommand(pageId, body.TemplateId, GetCurrentUserId()), ct);
        return result.IsSuccess ? StatusCode(StatusCodes.Status201Created, result.Value) : Failure(result.ErrorMessage, result.ErrorCode);
    }

    private IActionResult Failure(string? message, string? code) => code switch
    {
        ResultErrorCodes.NotFound => NotFound(new { error = message }),
        ResultErrorCodes.Conflict => Conflict(new { error = message, code = ResultErrorCodes.Conflict }),
        _ => BadRequest(new { error = message })
    };

    private Guid GetCurrentUserId() =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;
}

public sealed record CreateFrameTemplateRequest(string Name, LayoutFormat? LayoutFormat, IReadOnlyList<FrameSlot>? Slots);

public sealed record ApplyFrameTemplateRequest(Guid TemplateId);
