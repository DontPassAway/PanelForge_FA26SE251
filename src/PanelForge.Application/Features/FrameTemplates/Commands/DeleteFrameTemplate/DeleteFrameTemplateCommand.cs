using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;

namespace PanelForge.Application.Features.FrameTemplates.Commands.DeleteFrameTemplate;

/// <summary>Xoá mềm template của Workspace. Page đã áp dụng template không bị ảnh hưởng (Panel đã là bản sao độc lập).</summary>
public sealed record DeleteFrameTemplateCommand(Guid WorkspaceId, Guid TemplateId) : IRequest<Result<bool>>;

public sealed class DeleteFrameTemplateCommandHandler : IRequestHandler<DeleteFrameTemplateCommand, Result<bool>>
{
    private readonly IPanelForgeDbContext _db;

    public DeleteFrameTemplateCommandHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<bool>> Handle(DeleteFrameTemplateCommand cmd, CancellationToken ct)
    {
        // Template có sẵn của hệ thống (WorkspaceId null) không nằm trong DB nên tự rơi vào 404 ở đây
        var template = await _db.FrameTemplates
            .FirstOrDefaultAsync(t => t.Id == cmd.TemplateId && t.WorkspaceId == cmd.WorkspaceId, ct);
        if (template is null)
            return Result<bool>.Failure("Template không tồn tại trong Workspace này (template có sẵn của hệ thống không xoá được).",
                ResultErrorCodes.NotFound);

        _db.FrameTemplates.Remove(template); // DbContext chuyển thành soft-delete
        await _db.SaveChangesAsync(ct);
        return Result<bool>.Success(true);
    }
}
