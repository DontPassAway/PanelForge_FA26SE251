using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Assignments.Models;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Assignments.Queries.GetEditorReviewQueue;

/// <summary>
/// Truy vấn hàng đợi thẩm định của Editor xuyên suốt toàn bộ các Chapter trong một Series (Review Inbox).
/// Gom tất cả bài nộp đang ở Submitted / UnderReview và phân loại theo Gate 1 & Gate 2,
/// đồng thời kiểm tra quy tắc chống tự duyệt (4-Eyes Principle - Rule BR-26).
/// </summary>
public sealed record GetEditorReviewQueueQuery(
    Guid SeriesId,
    Guid ReviewerUserId,
    string? Gate = null, // "all" | "gate1" | "gate2"
    AssignmentStatus? Status = null
) : IRequest<Result<IReadOnlyList<EditorReviewQueueItemDto>>>;

public sealed class GetEditorReviewQueueQueryHandler
    : IRequestHandler<GetEditorReviewQueueQuery, Result<IReadOnlyList<EditorReviewQueueItemDto>>>
{
    private readonly IPanelForgeDbContext _db;

    public GetEditorReviewQueueQueryHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<IReadOnlyList<EditorReviewQueueItemDto>>> Handle(
        GetEditorReviewQueueQuery q, CancellationToken ct)
    {
        var series = await _db.Series.AsNoTracking().FirstOrDefaultAsync(s => s.Id == q.SeriesId && !s.IsDeleted, ct);
        if (series is null)
            return Result<IReadOnlyList<EditorReviewQueueItemDto>>.Failure($"Series {q.SeriesId} không tồn tại.", ResultErrorCodes.NotFound);

        var baseQuery = from a in _db.Assignments.AsNoTracking()
                        join c in _db.Chapters.AsNoTracking() on a.ChapterId equals c.Id
                        join u in _db.Users.AsNoTracking() on a.AssigneeUserId equals u.Id
                        join st in _db.PipelineStages.AsNoTracking() on a.PipelineStageId equals st.Id
                        where a.SeriesId == q.SeriesId && !a.IsDeleted && !c.IsDeleted
                        select new
                        {
                            Assignment = a,
                            c.ChapterNumber,
                            ChapterTitle = c.Title,
                            SeriesTitle = series.Title,
                            StageName = st.Name,
                            st.StageOrder,
                            st.ColorCode,
                            st.Slug,
                            st.IsApprovalGate,
                            AssigneeName = u.FullName,
                            AssigneeAvatar = u.AvatarUrl
                        };

        if (q.Status.HasValue)
        {
            baseQuery = baseQuery.Where(x => x.Assignment.Status == q.Status.Value);
        }
        else
        {
            // Mặc định gom tất cả các bài nộp cần thẩm định: Submitted hoặc UnderReview
            baseQuery = baseQuery.Where(x => x.Assignment.Status == AssignmentStatus.Submitted ||
                                             x.Assignment.Status == AssignmentStatus.UnderReview);
        }

        var rawList = await baseQuery
            .OrderByDescending(x => x.Assignment.Status == AssignmentStatus.Submitted) // Bài mới nộp lên trước
            .ThenByDescending(x => x.Assignment.Priority)
            .ThenBy(x => x.Assignment.DueDate == null)
            .ThenBy(x => x.Assignment.DueDate)
            .ThenBy(x => x.ChapterNumber)
            .ThenBy(x => x.StageOrder)
            .ToListAsync(ct);

        if (rawList.Count == 0)
            return Result<IReadOnlyList<EditorReviewQueueItemDto>>.Success(Array.Empty<EditorReviewQueueItemDto>());

        var assignmentIds = rawList.Select(x => x.Assignment.Id).ToList();

        // 1. Lấy Submission mới nhất của mỗi assignment
        var allSubmissions = await _db.Submissions.AsNoTracking()
            .Where(s => assignmentIds.Contains(s.AssignmentId))
            .OrderByDescending(s => s.AttemptNumber)
            .ToListAsync(ct);

        var latestSubmissions = allSubmissions
            .GroupBy(s => s.AssignmentId)
            .ToDictionary(g => g.Key, g => g.First());

        // 2. Kiểm tra BR-26: Người duyệt có từng nộp bài nào trong assignment này không
        var userSubmittedAssignmentIds = allSubmissions
            .Where(s => s.SubmittedByUserId == q.ReviewerUserId)
            .Select(s => s.AssignmentId)
            .ToHashSet();

        // 3. Đếm số lượng ChangeRequest chưa đóng (chưa Verified)
        var openChangeRequests = await _db.ChangeRequests.AsNoTracking()
            .Where(cr => assignmentIds.Contains(cr.AssignmentId) && cr.Status != ChangeRequestStatus.Verified)
            .GroupBy(cr => cr.AssignmentId)
            .Select(g => new { AssignmentId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.AssignmentId, x => x.Count, ct);

        // 4. Nhãn phạm vi (Trang / Khung / Cả chương) nạp theo lô
        var targetIds = rawList.Where(x => x.Assignment.EntityType != WorkflowEntityType.Chapter)
            .Select(x => x.Assignment.TargetEntityId).ToList();

        var targetPanels = await _db.Panels.AsNoTracking().Where(p => targetIds.Contains(p.Id))
            .Select(p => new { p.Id, p.PageId, p.PanelNumber, p.SceneId }).ToListAsync(ct);

        var pageIds = targetIds.Concat(targetPanels.Select(p => p.PageId)).Distinct().ToList();

        var pageNumbers = await _db.Pages.AsNoTracking().Where(p => pageIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.PageNumber, ct);

        var pagePanels = await _db.Panels.AsNoTracking().Where(p => pageIds.Contains(p.PageId) && p.SceneId != null)
            .Select(p => new { p.PageId, p.SceneId }).ToListAsync(ct);

        var sceneIds = pagePanels.Select(p => p.SceneId!.Value).Distinct().ToList();

        var sceneNumbers = await _db.Scenes.AsNoTracking().Where(s => sceneIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.SceneNumber, ct);

        string Scenes(IEnumerable<Guid?> sIds)
        {
            var numbers = sIds.Where(id => id != null && sceneNumbers.ContainsKey(id.Value))
                .Select(id => sceneNumbers[id!.Value]).Distinct().Order().ToList();
            return numbers.Count == 0 ? "" : $" · Cảnh {string.Join(", ", numbers)}";
        }

        string? ScopeLabel(Domain.Entities.Workflow.Assignment a)
        {
            switch (a.EntityType)
            {
                case WorkflowEntityType.Chapter:
                    return "Cả chương";
                case WorkflowEntityType.Page:
                    return pageNumbers.TryGetValue(a.TargetEntityId, out var pageNo)
                        ? $"Trang {pageNo}{Scenes(pagePanels.Where(p => p.PageId == a.TargetEntityId).Select(p => p.SceneId))}"
                        : null;
                case WorkflowEntityType.Panel:
                    var panel = targetPanels.FirstOrDefault(p => p.Id == a.TargetEntityId);
                    if (panel is null) return null;
                    var pNo = pageNumbers.TryGetValue(panel.PageId, out var n) ? $"Trang {n}" : "Trang ?";
                    var sc = panel.SceneId.HasValue && sceneNumbers.TryGetValue(panel.SceneId.Value, out var sn)
                        ? $" · Cảnh {sn}" : "";
                    return $"{pNo} · Khung {panel.PanelNumber}{sc}";
                default:
                    return null;
            }
        }

        string DetermineGate(string stageName, string? slug, int stageOrder)
        {
            var combined = $"{stageName} {slug}".ToLowerInvariant();
            if (combined.Contains("storyboard") || combined.Contains("name") || combined.Contains("layout") || combined.Contains("thumbnail"))
            {
                return "Gate 1: Storyboard";
            }
            return "Gate 2: Quality & Composite";
        }

        var results = new List<EditorReviewQueueItemDto>();

        foreach (var item in rawList)
        {
            var a = item.Assignment;
            var gate = DetermineGate(item.StageName, item.Slug, item.StageOrder);

            // Lọc theo cổng nếu có yêu cầu
            if (!string.IsNullOrWhiteSpace(q.Gate) && q.Gate != "all")
            {
                if (q.Gate.Equals("gate1", StringComparison.OrdinalIgnoreCase) && !gate.StartsWith("Gate 1"))
                    continue;
                if (q.Gate.Equals("gate2", StringComparison.OrdinalIgnoreCase) && !gate.StartsWith("Gate 2"))
                    continue;
            }

            latestSubmissions.TryGetValue(a.Id, out var latestSub);

            // Rule BR-26 (Anti-Self-Approval): Không được tự duyệt nếu mình là người được giao việc HOẶC từng nộp bài
            var isSelf = (a.AssigneeUserId == q.ReviewerUserId) || userSubmittedAssignmentIds.Contains(a.Id);

            results.Add(new EditorReviewQueueItemDto(
                AssignmentId: a.Id,
                ChapterId: a.ChapterId,
                ChapterNumber: item.ChapterNumber,
                ChapterTitle: item.ChapterTitle,
                SeriesId: a.SeriesId,
                SeriesTitle: item.SeriesTitle,
                PipelineStageId: a.PipelineStageId,
                StageName: item.StageName,
                StageOrder: item.StageOrder,
                StageColorCode: item.ColorCode,
                Gate: gate,
                Status: a.Status,
                EntityType: a.EntityType,
                TargetEntityId: a.TargetEntityId,
                ScopeLabel: ScopeLabel(a),
                AssigneeUserId: a.AssigneeUserId,
                AssigneeName: item.AssigneeName,
                AssigneeAvatarUrl: item.AssigneeAvatar,
                LatestSubmissionId: latestSub?.Id,
                SubmissionAttempt: latestSub?.AttemptNumber ?? 0,
                SubmittedAt: latestSub?.SubmittedAt,
                IsSelfSubmission: isSelf,
                OpenChangeRequestsCount: openChangeRequests.GetValueOrDefault(a.Id, 0),
                DueDate: a.DueDate,
                Priority: a.Priority,
                CreatedAt: a.CreatedAt,
                ArtworkReferenceIds: latestSub?.ArtworkReferenceIds
            ));
        }

        return Result<IReadOnlyList<EditorReviewQueueItemDto>>.Success(results);
    }
}
