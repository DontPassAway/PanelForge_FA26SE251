using MediatR;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.FrameTemplates.Models;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.FrameTemplates.Commands.CreateFrameTemplate;

/// <summary>Lưu bố cục khung thành template riêng của Workspace (vd Artist lưu bố cục trang đang làm để dùng lại).</summary>
public sealed record CreateFrameTemplateCommand(
    Guid WorkspaceId, Guid UserId, string Name, LayoutFormat LayoutFormat, IReadOnlyList<FrameSlot> Slots)
    : IRequest<Result<FrameTemplateDto>>;

public sealed class CreateFrameTemplateCommandHandler : IRequestHandler<CreateFrameTemplateCommand, Result<FrameTemplateDto>>
{
    private readonly IPanelForgeDbContext _db;

    public CreateFrameTemplateCommandHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<FrameTemplateDto>> Handle(CreateFrameTemplateCommand cmd, CancellationToken ct)
    {
        FrameTemplate template;
        try
        {
            template = FrameTemplate.Create(cmd.WorkspaceId, cmd.Name, cmd.LayoutFormat, cmd.Slots);
        }
        catch (ArgumentException ex)
        {
            return Result<FrameTemplateDto>.Failure(ex.Message, ResultErrorCodes.Validation);
        }

        template.CreatedBy = cmd.UserId.ToString();
        _db.FrameTemplates.Add(template);
        await _db.SaveChangesAsync(ct);
        return Result<FrameTemplateDto>.Success(FrameTemplateDto.From(template));
    }
}
