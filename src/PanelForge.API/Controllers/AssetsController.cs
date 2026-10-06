using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PanelForge.API.Common.Attributes;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Assets.Commands.DownloadAsset;
using PanelForge.Application.Features.Assets.Commands.IssueArtworkAccessLink;
using PanelForge.Application.Features.Assets.Commands.UploadArtwork;
using PanelForge.Application.Features.Assets.Models;
using PanelForge.Application.Features.Assets.Queries.GetPageArtwork;

namespace PanelForge.API.Controllers;

/// <summary>Upload và truy cập artwork (CF2 bước 10; BR-04, BR-13, BR-16).</summary>
[ApiController]
[Authorize]
public sealed class AssetsController : ControllerBase
{
    // 25 MB nội dung + dư cho phần còn lại của multipart
    private const long MaxRequestBytes = 26 * 1024 * 1024;

    private readonly IMediator _mediator;

    public AssetsController(IMediator mediator) => _mediator = mediator;

    private Guid GetCurrentUserId() =>
        Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : Guid.Empty;

    /// <summary>
    /// POST /api/chapters/{chapterId}/pages/{pageId}/artwork (multipart/form-data): file, stageId, clientSha256, layerType?.
    /// Server tính lại SHA-256 và từ chối nếu lệch (BR-04). Chỉ người đang giữ việc InProgress ở stage đó.
    /// </summary>
    [HttpPost("api/chapters/{chapterId:guid}/pages/{pageId:guid}/artwork")]
    [RequireWorkspaceRole]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxRequestBytes)]
    [ProducesResponseType(typeof(AssetReferenceDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> Upload(
        Guid chapterId, Guid pageId, [FromForm] UploadArtworkForm form, CancellationToken ct)
    {
        if (form.File is null || form.File.Length == 0)
            return BadRequest(new { error = "Thiếu file artwork." });

        using var buffer = new MemoryStream();
        await form.File.CopyToAsync(buffer, ct);

        var result = await _mediator.Send(new UploadArtworkCommand(
            chapterId, pageId, form.StageId, GetCurrentUserId(), buffer.ToArray(),
            form.File.FileName, form.ClientSha256, form.LayerType), ct);

        return result.IsSuccess
            ? CreatedAtAction(nameof(GetPageArtwork), new { chapterId, pageId }, result.Value)
            : Error(result);
    }

    /// <summary>GET /api/chapters/{chapterId}/pages/{pageId}/artwork?stageId= — các phiên bản artwork của Page (thành viên Workspace).</summary>
    [HttpGet("api/chapters/{chapterId:guid}/pages/{pageId:guid}/artwork")]
    [RequireWorkspaceRole]
    [ProducesResponseType(typeof(IReadOnlyList<AssetReferenceDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPageArtwork(Guid chapterId, Guid pageId, [FromQuery] Guid? stageId, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetPageArtworkQuery(chapterId, pageId, stageId), ct);
        return result.IsSuccess ? Ok(result.Value) : Error(result);
    }

    /// <summary>POST /api/chapters/{chapterId}/artwork/{assetReferenceId}/access-link — xin link tải ngắn hạn (BR-16).</summary>
    [HttpPost("api/chapters/{chapterId:guid}/artwork/{assetReferenceId:guid}/access-link")]
    [RequireWorkspaceRole]
    [ProducesResponseType(typeof(ArtworkAccessLinkDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> IssueAccessLink(Guid chapterId, Guid assetReferenceId, CancellationToken ct)
    {
        var result = await _mediator.Send(new IssueArtworkAccessLinkCommand(chapterId, assetReferenceId, GetCurrentUserId()), ct);
        return result.IsSuccess ? Ok(result.Value) : Error(result);
    }

    /// <summary>
    /// GET /api/assets/download?token= — tải artwork bằng link ngắn hạn. Không cần Bearer (dùng được trong thẻ img):
    /// quyền nằm ở token đã ký, và người nhận link phải còn là thành viên Workspace.
    /// </summary>
    [HttpGet("api/assets/download")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Download([FromQuery] string? token, CancellationToken ct)
    {
        var result = await _mediator.Send(
            new DownloadAssetCommand(token ?? string.Empty, HttpContext.Connection.RemoteIpAddress?.ToString()), ct);
        if (!result.IsSuccess) return Error(result);

        // Không để proxy/trình duyệt cache artwork chưa phát hành; không cho trình duyệt đoán lại loại file
        Response.Headers.CacheControl = "no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(result.Value!.Content, result.Value.ContentType, result.Value.FileName);
    }

    private IActionResult Error<T>(Result<T> result) => result.ErrorCode switch
    {
        ResultErrorCodes.NotFound => NotFound(new { error = result.ErrorMessage }),
        ResultErrorCodes.Conflict => Conflict(new { error = result.ErrorMessage, code = result.ErrorCode }),
        ResultErrorCodes.Forbidden => StatusCode(StatusCodes.Status403Forbidden, new { error = result.ErrorMessage, code = result.ErrorCode }),
        ResultErrorCodes.Unauthorized => Unauthorized(new { error = result.ErrorMessage }),
        _ => BadRequest(new { error = result.ErrorMessage })
    };
}

/// <summary>Form upload artwork. ClientSha256 là hash do client tính, chỉ để đối chiếu (BR-04).</summary>
public sealed class UploadArtworkForm
{
    public IFormFile? File { get; set; }
    public Guid StageId { get; set; }
    public string? ClientSha256 { get; set; }
    public string? LayerType { get; set; }
}
