using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.AiAgents.Common;
using PanelForge.Application.Features.AiAgents.Models;

namespace PanelForge.Application.Features.AiAgents.Queries.GetRun;

/// <summary>Trạng thái run + toàn bộ step (input/output từng lượt) + suggestion. Chỉ thành viên Workspace (BR-07, BR-23).</summary>
public sealed record GetRunQuery(Guid SeriesId, Guid RunId, Guid UserId) : IRequest<Result<AiRunDto>>;

public sealed class GetRunQueryHandler : IRequestHandler<GetRunQuery, Result<AiRunDto>>
{
    private readonly IPanelForgeDbContext _db;
    private readonly IWorkspaceAuthorizationService _auth;

    public GetRunQueryHandler(IPanelForgeDbContext db, IWorkspaceAuthorizationService auth)
    {
        _db = db;
        _auth = auth;
    }

    public async Task<Result<AiRunDto>> Handle(GetRunQuery q, CancellationToken ct)
    {
        var run = await _db.AiWorkflowRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == q.RunId && r.SeriesId == q.SeriesId, ct);
        if (run is null) return AiRunHelpers.Fail<AiRunDto>("Run không tồn tại trong Series.", ResultErrorCodes.NotFound);
        var viewerRole = await _auth.GetMemberRoleAsync(q.UserId, run.WorkspaceId, ct);
        if (viewerRole is null)
            return AiRunHelpers.Fail<AiRunDto>("Bạn không phải thành viên của Workspace này.", ResultErrorCodes.Forbidden);

        var steps = await _db.AiAgentSteps.AsNoTracking().Where(s => s.RunId == run.Id).OrderBy(s => s.StepIndex).ToListAsync(ct);
        var suggestions = await _db.AiSuggestions.AsNoTracking().Where(s => s.RunId == run.Id).OrderBy(s => s.CreatedAt).ToListAsync(ct);
        var ids = suggestions.Select(s => s.Id).ToList();
        var decisions = await _db.AiSuggestionDecisions.AsNoTracking().Where(d => ids.Contains(d.SuggestionId)).ToListAsync(ct);

        return Result<AiRunDto>.Success(new AiRunDto(
            run.Id, run.SeriesId, run.ChapterId, run.SceneId, run.RequestedByUserId, run.Status, run.Provider, run.Model,
            run.Brief, run.ReadingDirection, run.RevisionRounds, run.MaxRevisionRounds, run.BaseChapterVersion,
            run.BaseScriptRevisionId, AiRunHelpers.ReadGuidList(run.BibleRevisionIdsJson), run.ErrorCode, run.ErrorMessage,
            run.CreatedAt, run.LastActivityAtUtc, run.CompletedAtUtc,
            steps.Select(AiRunHelpers.ToDto).ToList(),
            suggestions.Select(s => AiRunHelpers.ToDto(s, decisions.Where(d => d.SuggestionId == s.Id), viewerRole)).ToList(),
            run.WorkflowType));
    }
}