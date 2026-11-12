using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PanelForge.API.Common.Attributes;
using PanelForge.Application.Common;
using PanelForge.Application.Features.AiAgents.Commands.ReviewSuggestion;
using PanelForge.Application.Features.AiAgents.Commands.RunCopilot;
using PanelForge.Application.Features.AiAgents.Commands.RunPublisherMetadata;
using PanelForge.Application.Features.AiAgents.Commands.RunThumbnail;
using PanelForge.Application.Features.AiAgents.Commands.RunWorkflow;
using PanelForge.Application.Features.AiAgents.Models;
using PanelForge.Application.Features.AiAgents.Queries.CompareRevisions;
using PanelForge.Application.Features.AiAgents.Queries.GetContentRevisions;
using PanelForge.Application.Features.AiAgents.Queries.GetProvenanceReport;
using PanelForge.Application.Features.AiAgents.Queries.GetRun;
using PanelForge.Application.Features.AiAgents.Queries.GetRunHistory;
using PanelForge.Application.Features.AiAgents.Queries.GetSuggestions;
using PanelForge.Application.Features.AiAgents.Queries.GetWorkflowAvailability;
using PanelForge.Domain.Enums;

namespace PanelForge.API.Controllers;

/// <summary>AI Multi-Agent demo (Writer → Continuity → Storyboard → Editor). Mọi kết quả chỉ là đề xuất chờ con người quyết định.</summary>
[ApiController]
[Authorize]
public sealed class AiAgentsController : ControllerBase
{
    private readonly IMediator _mediator;

    public AiAgentsController(IMediator mediator) => _mediator = mediator;

    private Guid CurrentUserId =>
        Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value, out var id) ? id : Guid.Empty;

    /// <summary>
    /// POST /api/series/{seriesId}/ai/workflows/run — chạy một nhiệm vụ AI (body.workflowType: FullScene | Script | Storyboard | Artist |
    /// Letterer | Editor | Producer). 202 + runId sau khi lưu run và AI service xác nhận nhận job.
    /// Quyền theo loại nhiệm vụ do Backend xác định (membership + vai trò + assignment + trạng thái khoá); filter chỉ yêu cầu là thành viên.
    /// Route /workflows/demo/run giữ để tương thích: không gửi workflowType = FullScene (hành vi cũ).
    /// </summary>
    [HttpPost("api/series/{seriesId:guid}/ai/workflows/run")]
    [HttpPost("api/series/{seriesId:guid}/ai/workflows/demo/run")]
    [RequireWorkspaceRole]
    [ProducesResponseType(typeof(RunWorkflowResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> RunWorkflow(
        [FromRoute] Guid seriesId, [FromBody] RunWorkflowRequest body, CancellationToken ct)
    {
        var result = await _mediator.Send(new RunWorkflowCommand(
            seriesId, CurrentUserId, body.ChapterId, body.SceneId, body.Brief, body.ReadingDirection,
            body.ChapterVersion, body.BaseScriptRevisionId, body.WorkflowType, body.PanelIds), ct);
        return result.IsSuccess
            ? Accepted($"/api/series/{seriesId}/ai/runs/{result.Value!.RunId}", result.Value)
            : ToError(result);
    }

    /// <summary>
    /// POST /api/series/{seriesId}/ai/copilot — Copilot sync trên MỘT dòng thoại (task: writer.rewrite | letterer.shorten).
    /// 200 + AiSuggestionDto (kind LineRewrite, Pending) để người dùng Accept/Reject như mọi đề xuất AI. Không ghi gì khi lỗi/timeout.
    /// </summary>
    [HttpPost("api/series/{seriesId:guid}/ai/copilot")]
    [RequireWorkspaceRole]
    [ProducesResponseType(typeof(AiSuggestionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> RunCopilot([FromRoute] Guid seriesId, [FromBody] RunCopilotRequest body, CancellationToken ct)
    {
        var result = await _mediator.Send(new RunCopilotCommand(
            seriesId, CurrentUserId, body.Task, body.LineId, body.Instruction, body.MaxChars), ct);
        return result.IsSuccess ? Ok(result.Value) : ToError(result);
    }

    /// <summary>
    /// POST /api/series/{seriesId}/ai/copilot/publisher-metadata — chỉ Producer (Publisher không dùng AI) xin gợi ý logline, synopsis, thể loại, teaser, age rating
    /// khi phát hành một Chapter. 200 + AiSuggestionDto (kind SeriesMetadata, Pending); Accept ghi synopsis + genre vào Series.
    /// </summary>
    [HttpPost("api/series/{seriesId:guid}/ai/copilot/publisher-metadata")]
    [RequireWorkspaceRole(WorkspaceRole.Producer)]
    [ProducesResponseType(typeof(AiSuggestionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> RunPublisherMetadata(
        [FromRoute] Guid seriesId, [FromBody] RunPublisherMetadataRequest body, CancellationToken ct)
    {
        var result = await _mediator.Send(new RunPublisherMetadataCommand(seriesId, body.ChapterId, CurrentUserId, body.Instruction), ct);
        return result.IsSuccess ? Ok(result.Value) : ToError(result);
    }

    /// <summary>
    /// POST /api/series/{seriesId}/ai/thumbnail — Artist sinh MỘT ảnh thumbnail cho panel từ prompt ảnh đã Accept (sync, 10–60 giây).
    /// 200 + AiSuggestionDto (kind Thumbnail, Pending); Accept tạo artwork phiên bản mới cho panel, ảnh cũ giữ nguyên.
    /// </summary>
    [HttpPost("api/series/{seriesId:guid}/ai/thumbnail")]
    [RequireWorkspaceRole]
    [ProducesResponseType(typeof(AiSuggestionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> RunThumbnail([FromRoute] Guid seriesId, [FromBody] RunThumbnailRequest body, CancellationToken ct)
    {
        var result = await _mediator.Send(new RunThumbnailCommand(seriesId, body.PanelId, CurrentUserId, body.Instruction), ct);
        return result.IsSuccess ? Ok(result.Value) : ToError(result);
    }

    /// <summary>GET /api/series/{seriesId}/ai/workflows/availability?chapterId=&amp;sceneId= — nhiệm vụ nào chạy được và còn thiếu điều kiện gì.</summary>
    [HttpGet("api/series/{seriesId:guid}/ai/workflows/availability")]
    [RequireWorkspaceRole]
    [ProducesResponseType(typeof(AiWorkflowAvailabilityResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAvailability(
        [FromRoute] Guid seriesId, [FromQuery] Guid chapterId, [FromQuery] Guid? sceneId, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetWorkflowAvailabilityQuery(seriesId, CurrentUserId, chapterId, sceneId), ct);
        return result.IsSuccess ? Ok(result.Value) : ToError(result);
    }

    /// <summary>GET /api/series/{seriesId}/ai/runs/{runId} — polling trạng thái, các step, suggestion.</summary>
    [HttpGet("api/series/{seriesId:guid}/ai/runs/{runId:guid}")]
    [RequireWorkspaceRole]
    [ProducesResponseType(typeof(AiRunDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRun([FromRoute] Guid seriesId, [FromRoute] Guid runId, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetRunQuery(seriesId, runId, CurrentUserId), ct);
        return result.IsSuccess ? Ok(result.Value) : ToError(result);
    }

    /// <summary>GET /api/series/{seriesId}/ai/suggestions?status=Pending&amp;runId=...</summary>
    [HttpGet("api/series/{seriesId:guid}/ai/suggestions")]
    [RequireWorkspaceRole]
    [ProducesResponseType(typeof(IReadOnlyList<AiSuggestionDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSuggestions(
        [FromRoute] Guid seriesId, [FromQuery] AiSuggestionStatus? status, [FromQuery] Guid? runId, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetSuggestionsQuery(seriesId, CurrentUserId, status, runId), ct);
        return result.IsSuccess ? Ok(result.Value) : ToError(result);
    }

    /// <summary>GET /api/series/{seriesId}/ai/scenes/{sceneId}/revisions — revision script/layout đã áp dụng + nguồn AI (provenance).</summary>
    [HttpGet("api/series/{seriesId:guid}/ai/scenes/{sceneId:guid}/revisions")]
    [RequireWorkspaceRole]
    [ProducesResponseType(typeof(IReadOnlyList<ContentRevisionDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSceneRevisions([FromRoute] Guid seriesId, [FromRoute] Guid sceneId, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetContentRevisionsQuery(seriesId, sceneId, CurrentUserId), ct);
        return result.IsSuccess ? Ok(result.Value) : ToError(result);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Lịch sử, So sánh, Provenance Report (CF4). Khôi phục script dùng
    // POST api/chapters/{chapterId}/scenes/{sceneId}/script-revisions/{revisionId}/restore (ScriptLinesController)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>GET /api/series/{seriesId}/ai/runs — lịch sử AI runs (lọc chapter/scene/status/type, phân trang).</summary>
    [HttpGet("api/series/{seriesId:guid}/ai/runs")]
    [RequireWorkspaceRole]
    [ProducesResponseType(typeof(AiRunHistoryResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRunHistory(
        [FromRoute] Guid seriesId,
        [FromQuery] Guid? chapterId,
        [FromQuery] Guid? sceneId,
        [FromQuery] AiRunStatus? status,
        [FromQuery] AiWorkflowType? workflowType,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(
            new GetAiRunHistoryQuery(seriesId, CurrentUserId, chapterId, sceneId, status, workflowType, page, pageSize), ct);
        return result.IsSuccess ? Ok(result.Value) : ToError(result);
    }

    /// <summary>GET /api/series/{seriesId}/ai/scenes/{sceneId}/revisions/compare?left=...&amp;right=... — diff giữa 2 revision.</summary>
    [HttpGet("api/series/{seriesId:guid}/ai/scenes/{sceneId:guid}/revisions/compare")]
    [RequireWorkspaceRole]
    [ProducesResponseType(typeof(RevisionDiffResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> CompareRevisions(
        [FromRoute] Guid seriesId,
        [FromRoute] Guid sceneId,
        [FromQuery] Guid left,
        [FromQuery] Guid right,
        CancellationToken ct)
    {
        var result = await _mediator.Send(
            new CompareRevisionsQuery(seriesId, sceneId, left, right, CurrentUserId), ct);
        return result.IsSuccess ? Ok(result.Value) : ToError(result);
    }

    /// <summary>
    /// GET /api/series/{seriesId}/ai/chapters/{chapterId}/provenance?sceneId= — UC-19: Provenance Report của chương
    /// (Run → Step → Suggestion → Decision → Revision → Snapshot). Producer; Editor xem chỉ đọc.
    /// </summary>
    [HttpGet("api/series/{seriesId:guid}/ai/chapters/{chapterId:guid}/provenance")]
    [RequireWorkspaceRole(WorkspaceRole.Producer, WorkspaceRole.Editor)]
    [ProducesResponseType(typeof(ProvenanceReportResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetProvenanceReport(
        [FromRoute] Guid seriesId,
        [FromRoute] Guid chapterId,
        [FromQuery] Guid? sceneId,
        CancellationToken ct)
    {
        var result = await _mediator.Send(new GetProvenanceReportQuery(seriesId, chapterId, sceneId, CurrentUserId), ct);
        return result.IsSuccess ? Ok(result.Value) : ToError(result);
    }

    // Quyền Accept/Reject/RequestChanges phụ thuộc LOẠI đề xuất (script/layout/prompt ảnh/thoại): handler kiểm theo AiWorkflowPolicy.ReviewRoles
    [HttpPost("api/series/{seriesId:guid}/ai/suggestions/{suggestionId:guid}/accept")]
    [RequireWorkspaceRole]
    [ProducesResponseType(typeof(ReviewSuggestionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<IActionResult> Accept(Guid seriesId, Guid suggestionId, [FromBody] ReviewSuggestionRequest body, CancellationToken ct)
        => Review(seriesId, suggestionId, AiSuggestionStatus.Accepted, body, ct);

    [HttpPost("api/series/{seriesId:guid}/ai/suggestions/{suggestionId:guid}/reject")]
    [RequireWorkspaceRole]
    [ProducesResponseType(typeof(ReviewSuggestionResponse), StatusCodes.Status200OK)]
    public Task<IActionResult> Reject(Guid seriesId, Guid suggestionId, [FromBody] ReviewSuggestionRequest body, CancellationToken ct)
        => Review(seriesId, suggestionId, AiSuggestionStatus.Rejected, body, ct);

    [HttpPost("api/series/{seriesId:guid}/ai/suggestions/{suggestionId:guid}/request-changes")]
    [RequireWorkspaceRole]
    [ProducesResponseType(typeof(ReviewSuggestionResponse), StatusCodes.Status200OK)]
    public Task<IActionResult> RequestChanges(Guid seriesId, Guid suggestionId, [FromBody] ReviewSuggestionRequest body, CancellationToken ct)
        => Review(seriesId, suggestionId, AiSuggestionStatus.ChangesRequested, body, ct);

    private async Task<IActionResult> Review(
        Guid seriesId, Guid suggestionId, AiSuggestionStatus decision, ReviewSuggestionRequest? body, CancellationToken ct)
    {
        var result = await _mediator.Send(new ReviewSuggestionCommand(
            seriesId, suggestionId, CurrentUserId, decision, body?.Reason, body?.ChapterVersion), ct);
        return result.IsSuccess ? Ok(result.Value) : ToError(result);
    }

    private IActionResult ToError<T>(Result<T> result)
    {
        var body = new { code = result.ErrorCode ?? "BAD_REQUEST", error = result.ErrorMessage };
        return result.ErrorCode switch
        {
            ResultErrorCodes.NotFound => NotFound(body),
            ResultErrorCodes.Conflict => Conflict(body),
            ResultErrorCodes.Forbidden or ResultErrorCodes.AiDisabled => StatusCode(StatusCodes.Status403Forbidden, body),
            ResultErrorCodes.Unauthorized => Unauthorized(body),
            ResultErrorCodes.AiQuotaExceeded => StatusCode(StatusCodes.Status429TooManyRequests, body),
            ResultErrorCodes.AiUnavailable => AiUnavailable(result),
            _ => BadRequest(body)
        };
    }

    /// <summary>503 { code: "AI_UNAVAILABLE", retryAfter } — Frontend ẩn nút AI, làm thủ công vẫn bình thường.</summary>
    private IActionResult AiUnavailable<T>(Result<T> result)
    {
        Response.Headers.RetryAfter = "30";
        return StatusCode(StatusCodes.Status503ServiceUnavailable,
            new { code = ResultErrorCodes.AiUnavailable, error = result.ErrorMessage, retryAfter = 30 });
    }
}