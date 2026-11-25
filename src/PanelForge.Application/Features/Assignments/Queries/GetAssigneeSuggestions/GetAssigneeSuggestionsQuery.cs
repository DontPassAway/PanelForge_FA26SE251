using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Assignments.Models;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Assignments.Queries.GetAssigneeSuggestions;

/// <summary>
/// Gợi ý người nhận khi giao việc ở một stage: chỉ thành viên Active đúng vai trò của stage (cùng điều kiện
/// AssignmentRules.ResolveAssigneeRoleAsync), xếp ít việc đang mở trước, rồi người đã làm stage này trong Series. Quy tắc tính được, không AI.
/// </summary>
public sealed record GetAssigneeSuggestionsQuery(Guid ChapterId, Guid PipelineStageId)
    : IRequest<Result<IReadOnlyList<AssigneeSuggestionDto>>>;

public sealed class GetAssigneeSuggestionsQueryHandler
    : IRequestHandler<GetAssigneeSuggestionsQuery, Result<IReadOnlyList<AssigneeSuggestionDto>>>
{
    private static readonly AssignmentStatus[] OpenStatuses =
        [AssignmentStatus.Assigned, AssignmentStatus.InProgress, AssignmentStatus.ReworkRequired, AssignmentStatus.Blocked];

    private readonly IPanelForgeDbContext _db;

    public GetAssigneeSuggestionsQueryHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<IReadOnlyList<AssigneeSuggestionDto>>> Handle(GetAssigneeSuggestionsQuery q, CancellationToken ct)
    {
        static Result<IReadOnlyList<AssigneeSuggestionDto>> Fail(string msg, string code) =>
            Result<IReadOnlyList<AssigneeSuggestionDto>>.Failure(msg, code);

        var chapter = await _db.Chapters.AsNoTracking().FirstOrDefaultAsync(c => c.Id == q.ChapterId, ct);
        if (chapter is null)
            return Fail($"Chapter {q.ChapterId} không tồn tại.", ResultErrorCodes.NotFound);

        var workspaceId = await _db.Series.AsNoTracking()
            .Where(s => s.Id == chapter.SeriesId).Select(s => s.WorkspaceId).FirstOrDefaultAsync(ct);

        var pipelineIds = await _db.PipelineDefinitions.AsNoTracking()
            .Where(d => d.SeriesId == chapter.SeriesId).Select(d => d.Id).ToListAsync(ct);
        var stage = await _db.PipelineStages.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == q.PipelineStageId && pipelineIds.Contains(s.PipelineDefinitionId), ct);
        if (stage is null)
            return Fail("Stage không thuộc pipeline của Series.", ResultErrorCodes.Validation);

        var allowedRole = stage.AllowedRole;
        var members = await _db.WorkspaceMembers.AsNoTracking()
            .Where(m => m.WorkspaceId == workspaceId && m.Status == WorkspaceMemberStatus.Active &&
                        (allowedRole == null || m.Role == allowedRole))
            .Select(m => new { m.UserId, m.Role })
            .ToListAsync(ct);
        if (members.Count == 0)
            return Result<IReadOnlyList<AssigneeSuggestionDto>>.Success([]);

        var userIds = members.Select(m => m.UserId).ToList();
        var users = await _db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.FullName, u.AvatarUrl })
            .ToDictionaryAsync(u => u.Id, ct);

        // Việc đang mở toàn Workspace + lịch sử stage này trong Series, gom theo người
        var workspaceSeriesIds = await _db.Series.AsNoTracking()
            .Where(s => s.WorkspaceId == workspaceId).Select(s => s.Id).ToListAsync(ct);
        var now = DateTime.UtcNow;
        var load = (await _db.Assignments.AsNoTracking()
                .Where(a => userIds.Contains(a.AssigneeUserId) && workspaceSeriesIds.Contains(a.SeriesId) && OpenStatuses.Contains(a.Status))
                .Select(a => new { a.AssigneeUserId, a.DueDate })
                .ToListAsync(ct))
            .GroupBy(a => a.AssigneeUserId)
            .ToDictionary(g => g.Key, g => (Open: g.Count(), Overdue: g.Count(a => a.DueDate < now)));
        var experience = (await _db.Assignments.AsNoTracking()
                .Where(a => userIds.Contains(a.AssigneeUserId) && a.PipelineStageId == stage.Id)
                .Select(a => new { a.AssigneeUserId, a.ChapterId })
                .ToListAsync(ct))
            .GroupBy(a => a.AssigneeUserId)
            .ToDictionary(g => g.Key, g => (Count: g.Count(), InChapter: g.Count(a => a.ChapterId == chapter.Id)));
        var seriesMembers = (await _db.SeriesMembers.AsNoTracking()
            .Where(m => m.SeriesId == chapter.SeriesId && userIds.Contains(m.UserId))
            .Select(m => m.UserId).ToListAsync(ct)).ToHashSet();

        var suggestions = members.Select(m =>
            {
                var (open, overdue) = load.GetValueOrDefault(m.UserId);
                var (exp, inChapter) = experience.GetValueOrDefault(m.UserId);
                var reasons = new List<string>();
                if (inChapter > 0) reasons.Add($"Đang nhận {inChapter} việc stage này trong chương");
                else if (exp > 0) reasons.Add($"Đã nhận {exp} việc stage này trong Series");
                if (open == 0) reasons.Add("Không có việc đang mở");
                if (overdue > 0) reasons.Add($"{overdue} việc quá hạn");
                var user = users.GetValueOrDefault(m.UserId);
                return new AssigneeSuggestionDto(
                    m.UserId, user?.FullName ?? "", user?.AvatarUrl, m.Role,
                    open, overdue, exp, seriesMembers.Contains(m.UserId), reasons);
            })
            // ponytail: xếp hạng cố định (quá hạn → tải → kinh nghiệm); cần trọng số tuỳ chỉnh thì đổi sang điểm số
            .OrderBy(s => s.OverdueCount > 0)
            .ThenBy(s => s.ActiveAssignmentCount)
            .ThenByDescending(s => s.StageExperienceCount)
            .ThenBy(s => s.FullName)
            .ToList();

        return Result<IReadOnlyList<AssigneeSuggestionDto>>.Success(suggestions);
    }
}
