using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.AI.Contracts;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.AiAgents.Common;

namespace PanelForge.Application.Features.AiAgents.Queries.GetAgentContext;

/// <summary>
/// API nội bộ cho AI service. Chỉ nhận RunId: scope và nội dung lấy từ snapshot đã lưu ở đầu run
/// (không có userId/workspaceId/seriesId do phía gọi tự khai báo, và Bible không đổi giữa chừng run).
/// </summary>
public sealed record GetAgentContextQuery(Guid RunId) : IRequest<Result<AgentContext>>;

public sealed class GetAgentContextQueryHandler : IRequestHandler<GetAgentContextQuery, Result<AgentContext>>
{
    private readonly IPanelForgeDbContext _db;

    public GetAgentContextQueryHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<AgentContext>> Handle(GetAgentContextQuery q, CancellationToken ct)
    {
        var run = await _db.AiWorkflowRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == q.RunId, ct);
        if (run is null) return AiRunHelpers.Fail<AgentContext>("Run không tồn tại.", ResultErrorCodes.NotFound);
        if (!run.IsActive) return AiRunHelpers.Fail<AgentContext>($"Run đã kết thúc ({run.Status}).", ResultErrorCodes.Conflict);

        var snapshot = AiRunHelpers.ReadSnapshot(run);
        if (snapshot is null) return AiRunHelpers.Fail<AgentContext>("Snapshot của run bị hỏng.", ResultErrorCodes.Validation);

        return Result<AgentContext>.Success(new AgentContext(
            run.Id, run.SeriesId, run.ChapterId, snapshot.ChapterNumber, run.SceneId ?? Guid.Empty, snapshot.SceneHeading,
            snapshot.SceneSummary, run.Brief, run.ReadingDirection.ToString(), run.BaseChapterVersion,
            run.Provider, run.Model, run.MaxRevisionRounds, snapshot.Bible,
            AiRunHelpers.ReadGuidList(run.BibleRevisionIdsJson), snapshot.BaselineScript,
            run.WorkflowType.ToString(), snapshot.ExistingLayout, snapshot.Artist, snapshot.Lettering, snapshot.Progress,
            snapshot.SeriesStoryArc, snapshot.ChapterOutline));
    }
}