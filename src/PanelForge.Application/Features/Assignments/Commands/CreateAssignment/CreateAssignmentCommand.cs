using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Assignments.Common;
using PanelForge.Application.Features.Assignments.Models;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Assignments.Commands.CreateAssignment;

/// <summary>UC-05 / CF2 bước 1. Thông báo cho người nhận (BR-14) làm ở đợt Notification riêng.</summary>
public sealed record CreateAssignmentCommand(
    Guid ChapterId,
    Guid UserId,
    WorkflowEntityType EntityType,
    Guid? TargetEntityId,
    Guid PipelineStageId,
    Guid AssigneeUserId,
    string Brief,
    string? ReferenceNotes,
    DateTime? DueDate,
    AssignmentPriority Priority,
    Guid? DependsOnAssignmentId,
    IReadOnlyList<Guid>? DependsOnAssignmentIds = null
) : IRequest<Result<AssignmentDto>>;

public sealed class CreateAssignmentCommandHandler : IRequestHandler<CreateAssignmentCommand, Result<AssignmentDto>>
{
    private readonly IPanelForgeDbContext _db;

    public CreateAssignmentCommandHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<AssignmentDto>> Handle(CreateAssignmentCommand cmd, CancellationToken ct)
    {
        static Result<AssignmentDto> Fail(string msg, string? code = ResultErrorCodes.Validation) =>
            Result<AssignmentDto>.Failure(msg, code);

        if (string.IsNullOrWhiteSpace(cmd.Brief))
            return Fail("Brief không được để trống.");

        var chapter = await _db.Chapters.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cmd.ChapterId, ct);
        if (chapter is null)
            return Fail($"Chapter {cmd.ChapterId} không tồn tại.", ResultErrorCodes.NotFound);

        if (AssignmentRules.ChapterNotEditableReason(chapter) is { } locked)
            return Fail(locked, ResultErrorCodes.Conflict);

        var workspaceId = await _db.Series.AsNoTracking()
            .Where(s => s.Id == chapter.SeriesId).Select(s => s.WorkspaceId).FirstOrDefaultAsync(ct);

        // Stage phải thuộc pipeline của chính Series (BR-03)
        var pipelineIds = await _db.PipelineDefinitions.AsNoTracking()
            .Where(d => d.SeriesId == chapter.SeriesId).Select(d => d.Id).ToListAsync(ct);
        var stage = await _db.PipelineStages.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == cmd.PipelineStageId && pipelineIds.Contains(s.PipelineDefinitionId), ct);
        if (stage is null)
            return Fail("Stage không thuộc pipeline của Series.");

        var target = await ResolveTargetAsync(chapter.Id, cmd, ct);
        if (target is null)
            return Fail("Đối tượng được giao không thuộc Chapter.");

        var role = await AssignmentRules.ResolveAssigneeRoleAsync(_db, workspaceId, cmd.AssigneeUserId, stage, ct);
        if (!role.IsSuccess) return Fail(role.ErrorMessage!, role.ErrorCode);

        var depIds = cmd.DependsOnAssignmentIds ?? (cmd.DependsOnAssignmentId.HasValue ? [cmd.DependsOnAssignmentId.Value] : null);
        if (await AssignmentRules.ValidateDependenciesAsync(_db, chapter.Id, null, depIds, ct) is { } depError)
            return Fail(depError);

        if (await AssignmentRules.ScopeOverlapReasonAsync(_db, chapter.Id, stage, cmd.EntityType, target.Value, ct) is { } overlap)
            return Fail(overlap, ResultErrorCodes.Conflict);

        // Tách trang/khung khỏi việc cả chương: báo người đang giữ cả chương là phần này đã chuyển cho người khác
        var whole = cmd.EntityType == WorkflowEntityType.Chapter ? null : await _db.Assignments.AsNoTracking()
            .FirstOrDefaultAsync(a => a.ChapterId == chapter.Id && a.PipelineStageId == stage.Id &&
                                      a.EntityType == WorkflowEntityType.Chapter, ct);

        var assignment = Assignment.Create(
            chapter.SeriesId, chapter.Id, cmd.EntityType, target.Value, stage.Id,
            cmd.AssigneeUserId, role.Value, cmd.Brief,
            referenceNotes: cmd.ReferenceNotes, dueDate: cmd.DueDate, priority: cmd.Priority,
            dependsOnAssignmentId: cmd.DependsOnAssignmentId,
            dependsOnAssignmentIds: depIds);
        assignment.CreatedBy = cmd.UserId.ToString();

        _db.Assignments.Add(assignment);
        // BR-14: thông báo ghi cùng lần Save với Assignment, không bao giờ có việc thiếu thông báo
        _db.Notifications.Add(AssignmentNotifications.Assigned(assignment));
        if (whole is not null && whole.AssigneeUserId != cmd.AssigneeUserId)
            _db.Notifications.Add(Notification.Create(whole.AssigneeUserId, NotificationType.AssignmentUnassigned,
                "Một phần việc cả chương đã chuyển cho người khác",
                $"{(cmd.EntityType == WorkflowEntityType.Page ? "Một trang" : "Một khung")} trong việc '{whole.Brief}' đã được giao riêng; bạn không còn sửa phần đó.",
                whole.Id, whole.ChapterId));
        await _db.SaveChangesAsync(ct);

        return Result<AssignmentDto>.Success(AssignmentDto.From(assignment));
    }

    /// <summary>Trả về id đối tượng nếu thuộc Chapter, ngược lại null.</summary>
    private async Task<Guid?> ResolveTargetAsync(Guid chapterId, CreateAssignmentCommand cmd, CancellationToken ct)
    {
        if (cmd.EntityType == WorkflowEntityType.Chapter)
            return cmd.TargetEntityId is null || cmd.TargetEntityId == chapterId ? chapterId : null;

        if (cmd.TargetEntityId is not { } id || id == Guid.Empty) return null;

        var pageIds = await _db.Pages.AsNoTracking()
            .Where(p => p.ChapterId == chapterId).Select(p => p.Id).ToListAsync(ct);

        var found = cmd.EntityType == WorkflowEntityType.Page
            ? pageIds.Contains(id)
            : await _db.Panels.AsNoTracking().AnyAsync(p => p.Id == id && pageIds.Contains(p.PageId), ct);

        return found ? id : null;
    }
}
