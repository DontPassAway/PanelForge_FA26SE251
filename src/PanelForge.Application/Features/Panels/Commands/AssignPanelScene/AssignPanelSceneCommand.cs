using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Chapters.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Panels.Models;
using PanelForge.Application.Features.Assignments.Common;

namespace PanelForge.Application.Features.Panels.Commands.AssignPanelScene;

/// <summary>V1.2: gán Panel vào tối đa một Scene (hoặc bỏ gán bằng null). Scene phải cùng Chapter với Page của Panel.</summary>
public sealed record AssignPanelSceneCommand(Guid PanelId, Guid? SceneId, Guid UserId) : IRequest<Result<PanelDto>>;

public sealed class AssignPanelSceneCommandHandler : IRequestHandler<AssignPanelSceneCommand, Result<PanelDto>>
{
    private readonly IPanelForgeDbContext _db;

    public AssignPanelSceneCommandHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<PanelDto>> Handle(AssignPanelSceneCommand command, CancellationToken ct)
    {
        var panel = await _db.Panels.Include(p => p.Page).FirstOrDefaultAsync(p => p.Id == command.PanelId, ct);
        if (panel is null)
            return Result<PanelDto>.Failure($"Panel {command.PanelId} không tồn tại.");
        if (await ChapterEditGuard.ReadOnlyReasonAsync(_db, panel.Page.ChapterId, ct) is { } readOnly)
            return Result<PanelDto>.Failure(readOnly, ResultErrorCodes.ChapterReadOnly);
        // Chỉ người giữ việc đang làm của phần này mới ghi (không đè phần đã chuyển giao cho người khác)
        if (await WorkScopeGuard.DenyReasonAsync(_db, command.UserId, panel.PageId, panel.Id, ct) is { } outOfScope)
            return Result<PanelDto>.Failure(outOfScope, ResultErrorCodes.Forbidden);

        if (command.SceneId is { } sceneId)
        {
            var scene = await _db.Scenes.AsNoTracking().FirstOrDefaultAsync(s => s.Id == sceneId, ct);
            if (scene is null)
                return Result<PanelDto>.Failure($"Scene {sceneId} không tồn tại.");
            if (scene.ChapterId != panel.Page.ChapterId)
                return Result<PanelDto>.Failure("Scene và Panel phải thuộc cùng một Chapter.");
        }

        panel.AssignToScene(command.SceneId);
        panel.UpdatedBy = command.UserId.ToString();
        await _db.SaveChangesAsync(ct);

        return Result<PanelDto>.Success(new PanelDto(
            panel.Id, panel.PageId, panel.PanelNumber, panel.BoundingBox, panel.ReadingOrder,
            panel.CurrentStageId, panel.CreatedAt, panel.UpdatedAt, panel.CreatedBy, panel.SceneId, panel.Version, panel.Direction));
    }
}
