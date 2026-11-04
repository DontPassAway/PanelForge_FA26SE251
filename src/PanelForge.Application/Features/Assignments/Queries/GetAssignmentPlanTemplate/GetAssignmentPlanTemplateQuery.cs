using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Assignments.Models;

namespace PanelForge.Application.Features.Assignments.Queries.GetAssignmentPlanTemplate;

/// <summary>
/// "Giao như chương trước": lấy đội đã nhận việc ở chương gần nhất (số chương nhỏ hơn, có việc chưa huỷ) của cùng Series.
/// Mỗi stage đang bật lấy người nhận nhiều việc nhất ở stage đó + brief gần nhất. Chỉ đọc; Producer sửa rồi gửi
/// POST …/assignments/plan, ở đó vai trò/thành viên được kiểm lại. Không lưu bảng mẫu riêng.
/// </summary>
public sealed record GetAssignmentPlanTemplateQuery(Guid ChapterId) : IRequest<Result<AssignmentPlanTemplateDto>>;

public sealed class GetAssignmentPlanTemplateQueryHandler : IRequestHandler<GetAssignmentPlanTemplateQuery, Result<AssignmentPlanTemplateDto>>
{
    private readonly IPanelForgeDbContext _db;

    public GetAssignmentPlanTemplateQueryHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<AssignmentPlanTemplateDto>> Handle(GetAssignmentPlanTemplateQuery q, CancellationToken ct)
    {
        var chapter = await _db.Chapters.AsNoTracking().FirstOrDefaultAsync(c => c.Id == q.ChapterId, ct);
        if (chapter is null)
            return Result<AssignmentPlanTemplateDto>.Failure($"Chapter {q.ChapterId} không tồn tại.", ResultErrorCodes.NotFound);

        var chaptersWithWork = await _db.Assignments.AsNoTracking()
            .Where(a => a.SeriesId == chapter.SeriesId && a.CancelReason == null && a.ChapterId != chapter.Id)
            .Select(a => a.ChapterId).Distinct().ToListAsync(ct);
        var source = await _db.Chapters.AsNoTracking()
            .Where(c => chaptersWithWork.Contains(c.Id) && c.ChapterNumber < chapter.ChapterNumber)
            .OrderByDescending(c => c.ChapterNumber)
            .Select(c => new { c.Id, c.ChapterNumber })
            .FirstOrDefaultAsync(ct);
        if (source is null)
            return Result<AssignmentPlanTemplateDto>.Success(new AssignmentPlanTemplateDto(null, null, []));

        var pipelineIds = await _db.PipelineDefinitions.AsNoTracking()
            .Where(d => d.SeriesId == chapter.SeriesId).Select(d => d.Id).ToListAsync(ct);
        var stages = await _db.PipelineStages.AsNoTracking()
            .Where(s => pipelineIds.Contains(s.PipelineDefinitionId) && s.IsEnabled)
            .ToDictionaryAsync(s => s.Id, ct);

        var work = await _db.Assignments.AsNoTracking()
            .Where(a => a.ChapterId == source.Id && a.CancelReason == null)
            .Select(a => new { a.PipelineStageId, a.AssigneeUserId, a.Brief, a.CreatedAt })
            .ToListAsync(ct);

        var picks = work.Where(w => stages.ContainsKey(w.PipelineStageId))
            .GroupBy(w => w.PipelineStageId)
            .Select(g =>
            {
                var top = g.GroupBy(w => w.AssigneeUserId).OrderByDescending(x => x.Count()).First();
                return (StageId: g.Key, AssigneeId: top.Key, Brief: top.MaxBy(w => w.CreatedAt)!.Brief);
            })
            .ToList();

        var userIds = picks.Select(p => p.AssigneeId).Distinct().ToList();
        var names = await _db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName, ct);

        var result = picks
            .Select(p => new AssignmentPlanTemplateStageDto(
                p.StageId, stages[p.StageId].Name, stages[p.StageId].StageOrder, p.AssigneeId,
                names.GetValueOrDefault(p.AssigneeId, ""), p.Brief))
            .OrderBy(s => s.StageOrder)
            .ToList();

        return Result<AssignmentPlanTemplateDto>.Success(new AssignmentPlanTemplateDto(source.Id, source.ChapterNumber, result));
    }
}
