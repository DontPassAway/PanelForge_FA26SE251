using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PanelForge.API.Common.Attributes;
using PanelForge.Application.Features.ConsistencyRules.Models;
using PanelForge.Application.Features.ConsistencyRules.Commands.CreateConsistencyRule;
using PanelForge.Application.Features.ConsistencyRules.Commands.UpdateConsistencyRule;
using PanelForge.Application.Features.ConsistencyRules.Commands.DeleteConsistencyRule;
using PanelForge.Application.Features.ConsistencyRules.Queries.GetConsistencyRules;
using PanelForge.Application.Features.Series.Commands.CreateSeries;
using PanelForge.Application.Features.Series.Commands.DeleteSeries;
using PanelForge.Application.Features.Series.Commands.SetSeriesCover;
using PanelForge.Application.Features.Series.Commands.SetSeriesStoryArc;
using PanelForge.Application.Features.Series.Queries.GetSeriesCover;
using PanelForge.Application.Features.Series.Commands.SetSeriesImage;
using PanelForge.Application.Features.Series.Queries.GetSeriesImage;
using PanelForge.Domain.Entities.Content;
using PanelForge.Application.Features.Series.Commands.UpdateSeries;
using PanelForge.Application.Features.Series.Models;
using PanelForge.Application.Features.Series.Queries.GetSeriesById;
using PanelForge.Application.Features.Series.Queries.GetSeriesList;
using PanelForge.Application.Features.TypographyPresets.Commands.CreateTypographyPreset;
using PanelForge.Application.Features.TypographyPresets.Commands.UpdateTypographyPreset;
using PanelForge.Application.Features.TypographyPresets.Commands.DeleteTypographyPreset;
using PanelForge.Application.Features.TypographyPresets.Models;
using PanelForge.Application.Features.TypographyPresets.Queries.GetTypographyPresets;
using PanelForge.Domain.Enums;
using PanelForge.API.Common;

namespace PanelForge.API.Controllers;

[ApiController]
[Authorize]
public sealed class SeriesController : ControllerBase
{
    private readonly IMediator _mediator;

    public SeriesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    private Guid GetCurrentUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value;
        return Guid.TryParse(claim, out var userId) ? userId : Guid.Empty;
    }

    /// <summary>
    /// POST /api/workspaces/{workspaceId}/series
    /// Tạo dự án truyện tranh mới trong Workspace (UC-03). 
    /// Task 7: Clones stages from PipelineTemplate (DB-backed).
    /// </summary>
    [HttpPost("api/workspaces/{workspaceId:guid}/series")]
    [RequireWorkspaceRole(WorkspaceRole.Producer)]
    [ProducesResponseType(typeof(SeriesDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreateSeries(
        [FromRoute] Guid workspaceId,
        [FromBody] CreateSeriesRequest body,
        CancellationToken cancellationToken)
    {
        var command = new CreateSeriesCommand(
            WorkspaceId: workspaceId,
            UserId: GetCurrentUserId(),
            Title: body.Title,
            Format: body.Format ?? "MANGA",
            ReadingDirection: body.ReadingDirection,
            Synopsis: body.Synopsis,
            Genre: body.Genre,
            StoryArc: body.StoryArc,
            PipelineTemplateId: body.PipelineTemplateId
        );

        var result = await _mediator.Send(command, cancellationToken);
        if (!result.IsSuccess)
        {
            if (result.ErrorMessage?.Contains("không tồn tại") == true)
                return NotFound(new { error = result.ErrorMessage });

            return BadRequest(new { error = result.ErrorMessage });
        }

        return CreatedAtAction(nameof(GetSeriesById), new { seriesId = result.Value!.Id }, result.Value);
    }

    /// <summary>
    /// GET /api/workspaces/{workspaceId}/series
    /// </summary>
    [HttpGet("api/workspaces/{workspaceId:guid}/series")]
    [RequireWorkspaceRole]
    [ProducesResponseType(typeof(IReadOnlyList<SeriesDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSeriesList(
        [FromRoute] Guid workspaceId,
        [FromQuery] string? search,
        CancellationToken cancellationToken)
    {
        var query = new GetSeriesListQuery(workspaceId, GetCurrentUserId(), search);
        var result = await _mediator.Send(query, cancellationToken);
        return Ok(result.Value);
    }

    /// <summary>
    /// GET /api/series/{seriesId}
    /// </summary>
    [HttpGet("api/series/{seriesId:guid}")]
    [RequireWorkspaceRole]
    [ProducesResponseType(typeof(SeriesDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSeriesById(
        [FromRoute] Guid seriesId,
        CancellationToken cancellationToken)
    {
        var query = new GetSeriesByIdQuery(seriesId);
        var result = await _mediator.Send(query, cancellationToken);

        if (!result.IsSuccess)
            return NotFound(new { error = result.ErrorMessage });

        return Ok(result.Value);
    }

    /// <summary>
    /// PUT /api/series/{seriesId}
    /// </summary>
    [HttpPut("api/series/{seriesId:guid}")]
    [RequireWorkspaceRole(WorkspaceRole.Producer)]
    [ProducesResponseType(typeof(SeriesDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateSeries(
        [FromRoute] Guid seriesId,
        [FromBody] UpdateSeriesRequest body,
        CancellationToken cancellationToken)
    {
        var command = new UpdateSeriesCommand(
            SeriesId: seriesId,
            UserId: GetCurrentUserId(),
            Title: body.Title,
            Synopsis: body.Synopsis,
            ReadingDirection: body.ReadingDirection,
            PipelineDefinitionId: body.PipelineDefinitionId,
            Genre: body.Genre,
            Format: body.Format,
            ReleaseScheduleJson: body.ReleaseScheduleJson,
            StoryArc: body.StoryArc
        );

        var result = await _mediator.Send(command, cancellationToken);
        if (!result.IsSuccess)
        {
            if (result.ErrorMessage?.Contains("không tồn tại") == true)
                return NotFound(new { error = result.ErrorMessage });

            return BadRequest(new { error = result.ErrorMessage });
        }

        return Ok(result.Value);
    }

    /// <summary>PUT /api/series/{seriesId}/story-arc — Producer/Writer ghi story arc (rỗng = xoá). Trả story arc đã lưu.</summary>
    [HttpPut("api/series/{seriesId:guid}/story-arc")]
    [RequireWorkspaceRole(WorkspaceRole.Producer, WorkspaceRole.Writer)]
    [ProducesResponseType(typeof(TextValueResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetStoryArc(
        [FromRoute] Guid seriesId, [FromBody] SetSeriesStoryArcRequest body, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new SetSeriesStoryArcCommand(seriesId, GetCurrentUserId(), body.StoryArc), cancellationToken);
        return result.IsSuccess ? Ok(new TextValueResponse(result.Value)) : NotFound(new { error = result.ErrorMessage });
    }

    /// <summary>
    /// PUT /api/series/{seriesId}/cover (multipart/form-data: file) — Producer đặt ảnh bìa (PNG/JPEG/WebP, ≤ 5 MB).
    /// Trả <c>value</c> = coverUrl mới (cũng có trong SeriesDto.coverUrl).
    /// </summary>
    [HttpPut("api/series/{seriesId:guid}/cover")]
    [RequireWorkspaceRole(WorkspaceRole.Producer)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(SetSeriesCoverCommandHandler.MaxBytes + 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = SetSeriesCoverCommandHandler.MaxBytes + 1024 * 1024)]
    [ProducesResponseType(typeof(TextValueResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetCover([FromRoute] Guid seriesId, IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { error = "Thiếu file ảnh bìa." });

        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, cancellationToken);
        return CoverResult(await _mediator.Send(new SetSeriesCoverCommand(seriesId, GetCurrentUserId(), buffer.ToArray()), cancellationToken));
    }

    /// <summary>DELETE /api/series/{seriesId}/cover — Producer xoá ảnh bìa (FE quay về bìa chữ cái).</summary>
    [HttpDelete("api/series/{seriesId:guid}/cover")]
    [RequireWorkspaceRole(WorkspaceRole.Producer)]
    [ProducesResponseType(typeof(TextValueResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ClearCover([FromRoute] Guid seriesId, CancellationToken cancellationToken)
        => CoverResult(await _mediator.Send(new SetSeriesCoverCommand(seriesId, GetCurrentUserId(), null), cancellationToken));

    /// <summary>
    /// GET /api/series/{seriesId}/cover — ảnh bìa hiện tại, mọi thành viên Workspace. Cần Bearer: FE tải bằng fetch rồi
    /// hiển thị qua blob URL. Dùng đúng <c>coverUrl</c> trong DTO (có <c>?v=</c> theo nội dung) để không dính cache bìa cũ.
    /// </summary>
    [HttpGet("api/series/{seriesId:guid}/cover")]
    [RequireWorkspaceRole]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCover([FromRoute] Guid seriesId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetSeriesCoverQuery(seriesId), cancellationToken);
        if (!result.IsSuccess)
            return NotFound(new { error = result.ErrorMessage });

        Response.Headers.CacheControl = "private, max-age=86400";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(result.Value!.Content, result.Value.ContentType);
    }

    private IActionResult CoverResult(PanelForge.Application.Common.Result<string?> result)
        => result.IsSuccess
            ? Ok(new TextValueResponse(result.Value))
            : result.ErrorCode == PanelForge.Application.Common.ResultErrorCodes.NotFound
                ? NotFound(new { error = result.ErrorMessage })
                : BadRequest(new { error = result.ErrorMessage });

    // ---- Avatar (vuông) & banner (ngang ~3:1) của Series: độc lập với bìa dọc; lưu qua IAssetStorage (R2) như bìa ----

    /// <summary>PUT /api/series/{seriesId}/{avatar|banner} (multipart: file) — Producer tải ảnh riêng (PNG/JPEG/WebP, ≤ 5 MB).</summary>
    [HttpPut("api/series/{seriesId:guid}/{kind:regex(^(avatar|banner)$)}")]
    [RequireWorkspaceRole(WorkspaceRole.Producer)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(SetSeriesImageCommandHandler.MaxBytes + 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = SetSeriesImageCommandHandler.MaxBytes + 1024 * 1024)]
    [ProducesResponseType(typeof(TextValueResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetImage([FromRoute] Guid seriesId, [FromRoute] string kind, IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { error = "Thiếu file ảnh." });

        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, cancellationToken);
        return CoverResult(await _mediator.Send(
            new SetSeriesImageCommand(seriesId, GetCurrentUserId(), ParseKind(kind), buffer.ToArray()), cancellationToken));
    }

    /// <summary>
    /// PUT /api/series/{seriesId}/{avatar|banner}/preset — body { presetId }. Backend đọc file mẫu từ danh mục có kiểm soát
    /// (Presets/series) rồi lưu lên kho asset (R2) theo đúng luồng tải ảnh; không nhận URL hay đường dẫn từ client.
    /// </summary>
    [HttpPut("api/series/{seriesId:guid}/{kind:regex(^(avatar|banner)$)}/preset")]
    [RequireWorkspaceRole(WorkspaceRole.Producer)]
    [ProducesResponseType(typeof(TextValueResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetImagePreset([FromRoute] Guid seriesId, [FromRoute] string kind,
        [FromBody] SeriesImagePresetRequest request, [FromServices] IWebHostEnvironment env, CancellationToken cancellationToken)
    {
        var imageKind = ParseKind(kind);
        var ids = imageKind == SeriesImageKind.Avatar ? SeriesImagePresets.AvatarIds : SeriesImagePresets.BannerIds;
        if (string.IsNullOrWhiteSpace(request?.PresetId) || !ids.Contains(request.PresetId))
            return BadRequest(new { error = "Mẫu ảnh không tồn tại.", code = "INVALID_SERIES_IMAGE_PRESET" });

        // ID đã nằm trong danh mục cố định nên đường dẫn không thể bị điều khiển từ client
        var path = Path.Combine(env.ContentRootPath, "Presets", "series",
            imageKind == SeriesImageKind.Avatar ? "avatars" : "banners", request.PresetId + ".webp");
        if (!System.IO.File.Exists(path))
            return NotFound(new { error = "Không tìm thấy file ảnh mẫu trên máy chủ." });

        var bytes = await System.IO.File.ReadAllBytesAsync(path, cancellationToken);
        return CoverResult(await _mediator.Send(
            new SetSeriesImageCommand(seriesId, GetCurrentUserId(), imageKind, bytes), cancellationToken));
    }

    /// <summary>DELETE /api/series/{seriesId}/{avatar|banner} — Producer xoá (FE về fallback). File cũ giữ nguyên trên kho.</summary>
    [HttpDelete("api/series/{seriesId:guid}/{kind:regex(^(avatar|banner)$)}")]
    [RequireWorkspaceRole(WorkspaceRole.Producer)]
    [ProducesResponseType(typeof(TextValueResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ClearImage([FromRoute] Guid seriesId, [FromRoute] string kind, CancellationToken cancellationToken)
        => CoverResult(await _mediator.Send(
            new SetSeriesImageCommand(seriesId, GetCurrentUserId(), ParseKind(kind), null), cancellationToken));

    /// <summary>GET /api/series/{seriesId}/{avatar|banner} — mọi thành viên Workspace; dùng URL trong DTO (có ?v= theo nội dung).</summary>
    [HttpGet("api/series/{seriesId:guid}/{kind:regex(^(avatar|banner)$)}")]
    [RequireWorkspaceRole]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetImage([FromRoute] Guid seriesId, [FromRoute] string kind, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetSeriesImageQuery(seriesId, ParseKind(kind)), cancellationToken);
        if (!result.IsSuccess)
            return NotFound(new { error = result.ErrorMessage });

        Response.Headers.CacheControl = "private, max-age=86400";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(result.Value!.Content, result.Value.ContentType);
    }

    private static SeriesImageKind ParseKind(string kind)
        => kind == "avatar" ? SeriesImageKind.Avatar : SeriesImageKind.Banner;

    /// <summary>
    /// DELETE /api/series/{seriesId}
    /// </summary>
    [HttpDelete("api/series/{seriesId:guid}")]
    [RequireWorkspaceRole(WorkspaceRole.Producer)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteSeries(
        [FromRoute] Guid seriesId,
        CancellationToken cancellationToken)
    {
        var command = new DeleteSeriesCommand(seriesId, GetCurrentUserId());
        var result = await _mediator.Send(command, cancellationToken);

        if (!result.IsSuccess)
            return NotFound(new { error = result.ErrorMessage });

        return Ok(new { message = $"Series {seriesId} đã được xóa thành công." });
    }

    // ─── Task 8: Typography Presets (replaces old SeriesPreset JSON endpoint) ──

    /// <summary>
    /// GET /api/series/{seriesId}/presets
    /// Lấy danh sách TypographyPresets của Series (Task 8, NFR-08).
    /// </summary>
    [HttpGet("api/series/{seriesId:guid}/presets")]
    [RequireWorkspaceRole]
    [ProducesResponseType(typeof(IReadOnlyList<TypographyPresetDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTypographyPresets(
        [FromRoute] Guid seriesId,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetTypographyPresetsQuery(seriesId), cancellationToken);
        return Ok(result.Value);
    }

    /// <summary>
    /// POST /api/series/{seriesId}/presets
    /// </summary>
    [HttpPost("api/series/{seriesId:guid}/presets")]
    [RequireWorkspaceRole(WorkspaceRole.Producer)]
    [ProducesResponseType(typeof(TypographyPresetDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateTypographyPreset(
        [FromRoute] Guid seriesId,
        [FromBody] CreateTypographyPresetRequest body,
        CancellationToken cancellationToken)
    {
        var command = new CreateTypographyPresetCommand(
            SeriesId: seriesId,
            RequestingUserId: GetCurrentUserId(),
            Name: body.Name,
            FontFamily: body.FontFamily,
            FontSize: body.FontSize,
            FontWeight: body.FontWeight,
            FontStyle: body.FontStyle,
            LineHeight: body.LineHeight,
            LetterSpacing: body.LetterSpacing,
            TextAlign: body.TextAlign,
            UsageType: body.UsageType,
            IsDefault: body.IsDefault
        );
        var result = await _mediator.Send(command, cancellationToken);
        if (!result.IsSuccess) return BadRequest(new { error = result.ErrorMessage });
        return StatusCode(StatusCodes.Status201Created, result.Value);
    }

    /// <summary>
    /// PUT /api/series/{seriesId}/presets/{presetId}
    /// </summary>
    [HttpPut("api/series/{seriesId:guid}/presets/{presetId:guid}")]
    [RequireWorkspaceRole(WorkspaceRole.Producer)]
    [ProducesResponseType(typeof(TypographyPresetDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateTypographyPreset(
        [FromRoute] Guid seriesId,
        [FromRoute] Guid presetId,
        [FromBody] UpdateTypographyPresetRequest body,
        CancellationToken cancellationToken)
    {
        var command = new UpdateTypographyPresetCommand(
            PresetId: presetId,
            SeriesId: seriesId,
            RequestingUserId: GetCurrentUserId(),
            Name: body.Name,
            FontFamily: body.FontFamily,
            FontSize: body.FontSize,
            FontWeight: body.FontWeight,
            FontStyle: body.FontStyle,
            LineHeight: body.LineHeight,
            LetterSpacing: body.LetterSpacing,
            TextAlign: body.TextAlign,
            UsageType: body.UsageType,
            IsDefault: body.IsDefault
        );
        var result = await _mediator.Send(command, cancellationToken);
        if (!result.IsSuccess) return BadRequest(new { error = result.ErrorMessage });
        return Ok(result.Value);
    }

    /// <summary>
    /// DELETE /api/series/{seriesId}/presets/{presetId}
    /// </summary>
    [HttpDelete("api/series/{seriesId:guid}/presets/{presetId:guid}")]
    [RequireWorkspaceRole(WorkspaceRole.Producer)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> DeleteTypographyPreset(
        [FromRoute] Guid seriesId,
        [FromRoute] Guid presetId,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new DeleteTypographyPresetCommand(presetId, seriesId, GetCurrentUserId()), cancellationToken);
        if (!result.IsSuccess) return BadRequest(new { error = result.ErrorMessage });
        return Ok(new { message = result.Value });
    }

    // ─── Task 9: Consistency Rules (replaces old SeriesPreset JSON endpoint) ───

    /// <summary>
    /// GET /api/series/{seriesId}/consistency-rules
    /// </summary>
    [HttpGet("api/series/{seriesId:guid}/consistency-rules")]
    [RequireWorkspaceRole]
    [ProducesResponseType(typeof(IReadOnlyList<ConsistencyRuleDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetConsistencyRules(
        [FromRoute] Guid seriesId,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetConsistencyRulesQuery(seriesId), cancellationToken);
        return Ok(result.Value);
    }

    /// <summary>
    /// POST /api/series/{seriesId}/consistency-rules
    /// </summary>
    [HttpPost("api/series/{seriesId:guid}/consistency-rules")]
    [RequireWorkspaceRole(WorkspaceRole.Producer)]
    [ProducesResponseType(typeof(ConsistencyRuleDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateConsistencyRule(
        [FromRoute] Guid seriesId,
        [FromBody] CreateConsistencyRuleRequest body,
        CancellationToken cancellationToken)
    {
        var command = new CreateConsistencyRuleCommand(
            SeriesId: seriesId,
            RequestingUserId: GetCurrentUserId(),
            Name: body.Name,
            RuleType: body.RuleType,
            Description: body.Description,
            IsEnabled: body.IsEnabled,
            Pattern: body.Pattern,
            Severity: body.Severity,
            ConfigurationJson: body.ConfigurationJson
        );
        var result = await _mediator.Send(command, cancellationToken);
        if (!result.IsSuccess) return BadRequest(new { error = result.ErrorMessage });
        return StatusCode(StatusCodes.Status201Created, result.Value);
    }

    /// <summary>
    /// PUT /api/series/{seriesId}/consistency-rules/{ruleId}
    /// </summary>
    [HttpPut("api/series/{seriesId:guid}/consistency-rules/{ruleId:guid}")]
    [RequireWorkspaceRole(WorkspaceRole.Producer)]
    [ProducesResponseType(typeof(ConsistencyRuleDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateConsistencyRule(
        [FromRoute] Guid seriesId,
        [FromRoute] Guid ruleId,
        [FromBody] UpdateConsistencyRuleRequest body,
        CancellationToken cancellationToken)
    {
        var command = new UpdateConsistencyRuleCommand(
            RuleId: ruleId,
            SeriesId: seriesId,
            RequestingUserId: GetCurrentUserId(),
            Name: body.Name,
            RuleType: body.RuleType,
            Description: body.Description,
            IsEnabled: body.IsEnabled,
            Pattern: body.Pattern,
            Severity: body.Severity,
            ConfigurationJson: body.ConfigurationJson
        );
        var result = await _mediator.Send(command, cancellationToken);
        if (!result.IsSuccess) return BadRequest(new { error = result.ErrorMessage });
        return Ok(result.Value);
    }

    /// <summary>
    /// DELETE /api/series/{seriesId}/consistency-rules/{ruleId}
    /// </summary>
    [HttpDelete("api/series/{seriesId:guid}/consistency-rules/{ruleId:guid}")]
    [RequireWorkspaceRole(WorkspaceRole.Producer)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> DeleteConsistencyRule(
        [FromRoute] Guid seriesId,
        [FromRoute] Guid ruleId,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new DeleteConsistencyRuleCommand(ruleId, seriesId, GetCurrentUserId()), cancellationToken);
        if (!result.IsSuccess) return BadRequest(new { error = result.ErrorMessage });
        return Ok(new { message = result.Value });
    }
}

public sealed record SetSeriesStoryArcRequest(string? StoryArc);

public sealed record SeriesImagePresetRequest(string? PresetId);
