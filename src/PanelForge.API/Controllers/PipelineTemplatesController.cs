using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PanelForge.Application.Features.MasterData.PipelineTemplates.Commands;
using PanelForge.Application.Features.MasterData.PipelineTemplates.Queries;

namespace PanelForge.API.Controllers;

/// <summary>
/// UC-03 bước 3: Producer chọn PipelineTemplate khi tạo Series.
/// Chỉ đọc và chỉ template đang hoạt động; quản lý template vẫn ở /api/Admin/pipeline-templates.
/// </summary>
[ApiController]
[Route("api/pipeline-templates")]
[Authorize]
public class PipelineTemplatesController : ControllerBase
{
    private readonly IMediator _mediator;

    public PipelineTemplatesController(IMediator mediator) => _mediator = mediator;

    /// <summary>GET /api/pipeline-templates — template đang hoạt động, template mặc định đứng đầu.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<PipelineTemplateDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetActiveTemplates(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetPipelineTemplatesQuery(ActiveOnly: true), cancellationToken);
        return Ok(result.Value);
    }
}
