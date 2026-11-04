using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PanelForge.AI.Contracts;
using PanelForge.API.Common.Attributes;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.AiAgents.Commands.RecordAgentStep;
using PanelForge.Application.Features.AiAgents.Queries.GetAgentContext;

namespace PanelForge.API.Controllers;

/// <summary>
/// API nội bộ cho service PanelForge.AI (không dành cho Frontend). Xác thực bằng X-Service-Key + loopback, KHÔNG dùng JWT.
/// Quyền/scope luôn suy ra từ run đã lưu, không nhận userId/workspaceId/seriesId từ phía gọi.
/// </summary>
[ApiController]
[AllowAnonymous]
[ServiceKeyAuth]
[Route("internal/ai/runs/{runId:guid}")]
public sealed class InternalAiController : ControllerBase
{
    private readonly IMediator _mediator;

    public InternalAiController(IMediator mediator) => _mediator = mediator;

    [HttpGet("context")]
    [ProducesResponseType(typeof(AgentContext), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetContext([FromRoute] Guid runId, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetAgentContextQuery(runId), ct);
        return result.IsSuccess ? Ok(result.Value) : ToError(result);
    }

    [HttpPost("steps")]
    [ProducesResponseType(typeof(AgentStepAck), StatusCodes.Status200OK)]
    public async Task<IActionResult> RecordStep([FromRoute] Guid runId, [FromBody] AgentStepReport report, CancellationToken ct)
    {
        var result = await _mediator.Send(new RecordAgentStepCommand(runId, report), ct);
        return result.IsSuccess ? Ok(result.Value) : ToError(result);
    }

    [HttpPost("stream-token")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> StreamToken(
        [FromRoute] Guid runId, [FromBody] StreamTokenPayload payload, [FromServices] IAiStreamPublisher publisher, CancellationToken ct)
    {
        await publisher.PublishTokenChunkAsync(runId, payload, ct);
        return Ok();
    }

    private IActionResult ToError<T>(Result<T> result)
    {
        var body = new { code = result.ErrorCode ?? "BAD_REQUEST", error = result.ErrorMessage };
        return result.ErrorCode switch
        {
            ResultErrorCodes.NotFound => NotFound(body),
            ResultErrorCodes.Conflict => Conflict(body),
            _ => BadRequest(body)
        };
    }
}