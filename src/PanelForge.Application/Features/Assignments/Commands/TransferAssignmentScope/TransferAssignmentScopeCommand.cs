using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Assignments.Common;
using PanelForge.Application.Features.Assignments.Models;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Assignments.Commands.TransferAssignmentScope;

/// <summary>
/// Chuyển giao một phần việc cả chương (stage Artist/Letterer) sang người khác: vd A giữ cả Chương 1, Editor trả sửa Trang 5–6,
/// Producer chuyển riêng Trang 5–6 cho B. Mỗi trang/khung thành một việc riêng của B; việc cả chương của A co lại (phạm vi
/// thực tế trừ phần đã tách, StageProgressRules.ResolvePanelsAsync), nên A mất quyền sửa/nộp đúng phần đó và giữ phần còn lại.
/// Yêu cầu sửa còn mở gắn với Panel được chuyển đi theo B; lịch sử nộp, tác giả phiên bản và yêu cầu đã đóng giữ ở việc của A.
/// Khác với tạo việc mới chồng phạm vi (luôn bị chặn): đây là nghiệp vụ chuyển giao rõ ràng, bắt buộc lý do.
/// </summary>
public sealed record TransferAssignmentScopeCommand(
    Guid ChapterId,
    Guid AssignmentId,
    Guid UserId,
    WorkflowEntityType TargetType,
    IReadOnlyList<Guid> TargetIds,
    Guid NewAssigneeUserId,
    string Reason,
    string? Brief = null,
    DateTime? DueDate = null) : IRequest<Result<IReadOnlyList<AssignmentDto>>>;

public sealed class TransferAssignmentScopeCommandHandler
    : IRequestHandler<TransferAssignmentScopeCommand, Result<IReadOnlyList<AssignmentDto>>>
{
    private readonly IPanelForgeDbContext _db;

    public TransferAssignmentScopeCommandHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<IReadOnlyList<AssignmentDto>>> Handle(TransferAssignmentScopeCommand cmd, CancellationToken ct)
    {
        static Result<IReadOnlyList<AssignmentDto>> Fail(string msg, string code = ResultErrorCodes.Validation) =>
            Result<IReadOnlyList<AssignmentDto>>.Failure(msg, code);

        if (string.IsNullOrWhiteSpace(cmd.Reason))
            return Fail("Chuyển giao bắt buộc có lý do.");
        if (cmd.TargetType is not (WorkflowEntityType.Page or WorkflowEntityType.Panel))
            return Fail("Chỉ chuyển giao theo trang hoặc khung.");
        var targetIds = (cmd.TargetIds ?? []).Where(id => id != Guid.Empty).Distinct().ToList();
        if (targetIds.Count == 0)
            return Fail("Chọn ít nhất một trang/khung để chuyển giao.");

        var source = await _db.Assignments
            .FirstOrDefaultAsync(a => a.Id == cmd.AssignmentId && a.ChapterId == cmd.ChapterId, ct);
        if (source is null)
            return Fail($"Assignment {cmd.AssignmentId} không tồn tại.", ResultErrorCodes.NotFound);

        var chapter = await _db.Chapters.AsNoTracking().FirstAsync(c => c.Id == cmd.ChapterId, ct);
        if (AssignmentRules.ChapterNotEditableReason(chapter) is { } locked)
            return Fail(locked, ResultErrorCodes.Conflict);

        if (source.EntityType != WorkflowEntityType.Chapter || !StageProgressRules.IsProductionRole(source.AssignedRole))
            return Fail("Chỉ chuyển giao một phần từ việc cả chương của Artist/Letterer; đổi người cả việc thì sửa người nhận của việc.");

        switch (source.Status)
        {
            case AssignmentStatus.Submitted or AssignmentStatus.UnderReview:
                return Fail("Việc cả chương đang có lượt nộp chờ duyệt: Editor cần duyệt hoặc trả sửa lượt nộp đó trước khi chuyển giao.",
                    ResultErrorCodes.Conflict);
            case AssignmentStatus.Approved:
                return Fail("Việc cả chương đã được duyệt: phải mở lại hợp lệ trước khi chuyển giao phần cần sửa.", ResultErrorCodes.Conflict);
        }

        if (cmd.NewAssigneeUserId == source.AssigneeUserId)
            return Fail("Người nhận mới trùng người đang giữ việc.");

        var stage = await _db.PipelineStages.AsNoTracking().FirstAsync(s => s.Id == source.PipelineStageId, ct);
        var workspaceId = await _db.Series.AsNoTracking()
            .Where(s => s.Id == chapter.SeriesId).Select(s => s.WorkspaceId).FirstOrDefaultAsync(ct);
        var role = await AssignmentRules.ResolveAssigneeRoleAsync(_db, workspaceId, cmd.NewAssigneeUserId, stage, ct);
        if (!role.IsSuccess) return Fail(role.ErrorMessage!, role.ErrorCode!);

        // Phần được chuyển phải đang nằm trong phạm vi thực tế của việc cả chương
        var inScope = (await StageProgressRules.ResolvePanelsAsync(_db, source, ct)).ToList();
        var carved = await StageProgressRules.CarvedOutAsync(_db, chapter.Id, stage.Id, ct);
        var pageIds = await _db.Pages.AsNoTracking().Where(p => p.ChapterId == chapter.Id).Select(p => p.Id).ToListAsync(ct);
        var scopes = new List<(Guid Target, HashSet<Guid> Panels)>();
        foreach (var id in targetIds)
        {
            HashSet<Guid> panels;
            if (cmd.TargetType == WorkflowEntityType.Page)
            {
                if (!pageIds.Contains(id)) return Fail("Trang được chuyển giao không thuộc chương này.");
                if (carved.Pages.Contains(id)) return Fail("Trang này đã được chuyển giao trước đó.", ResultErrorCodes.Conflict);
                panels = (await _db.Panels.AsNoTracking().Where(p => p.PageId == id).Select(p => p.Id).ToListAsync(ct)).ToHashSet();
                if (panels.Any(p => carved.Panels.Contains(p)))
                    return Fail("Trang này có khung đã chuyển giao cho người khác; chuyển giao từng khung còn lại.", ResultErrorCodes.Conflict);
            }
            else
            {
                if (!inScope.Any(p => p.Id == id))
                    return Fail("Khung được chuyển giao không thuộc phạm vi đang giữ của việc cả chương.", ResultErrorCodes.Conflict);
                panels = [id];
            }

            if (await AssignmentRules.ScopeOverlapReasonAsync(_db, chapter.Id, stage, cmd.TargetType, id, ct, transfer: true) is { } overlap)
                return Fail(overlap, ResultErrorCodes.Conflict);
            scopes.Add((id, panels));
        }

        var reason = cmd.Reason.Trim();
        var created = new List<Assignment>();
        foreach (var (target, panels) in scopes)
        {
            var part = Assignment.Create(
                source.SeriesId, source.ChapterId, cmd.TargetType, target, source.PipelineStageId,
                cmd.NewAssigneeUserId, role.Value, string.IsNullOrWhiteSpace(cmd.Brief) ? $"{source.Brief} (chuyển giao: {reason})" : cmd.Brief,
                referenceNotes: source.ReferenceNotes, referenceAssetUrlsJson: source.ReferenceAssetUrlsJson,
                dueDate: cmd.DueDate ?? source.DueDate, priority: source.Priority,
                dependsOnAssignmentIds: source.GetDependsOnAssignmentIds());
            part.CreatedBy = cmd.UserId.ToString();
            // Bị trả sửa rồi mới chuyển: phần chuyển đi vẫn là việc phải làm lại, kèm phản hồi cũ
            if (source.Status == AssignmentStatus.ReworkRequired)
                part.RequestRework(source.LastFeedbackComment ?? reason);

            _db.Assignments.Add(part);
            await ChangeRequestRules.TransferOpenAsync(_db, source, part, panels, ct);
            _db.Notifications.Add(AssignmentNotifications.Assigned(part));
            created.Add(part);
        }

        // Đổi updated_at của việc nguồn để nộp/duyệt đang chạy song song trên nó bị 409 thay vì phủ lên phạm vi mới
        source.UpdatedAt = DateTime.UtcNow;
        source.UpdatedBy = cmd.UserId.ToString();
        _db.Notifications.Add(Notification.Create(source.AssigneeUserId, NotificationType.AssignmentUnassigned,
            "Một phần việc cả chương đã chuyển cho người khác",
            $"{created.Count} {(cmd.TargetType == WorkflowEntityType.Page ? "trang" : "khung")} trong việc '{source.Brief}' đã chuyển giao ({reason}); bạn không còn sửa/nộp phần đó.",
            source.Id, source.ChapterId));

        await _db.SaveChangesAsync(ct);
        return Result<IReadOnlyList<AssignmentDto>>.Success(created.Select(AssignmentDto.From).ToList());
    }
}
