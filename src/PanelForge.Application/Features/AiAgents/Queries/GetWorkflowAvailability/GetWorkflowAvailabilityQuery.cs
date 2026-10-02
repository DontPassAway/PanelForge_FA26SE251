using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.AI.Contracts;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.AiAgents.Common;
using PanelForge.Application.Features.AiAgents.Models;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.AiAgents.Queries.GetWorkflowAvailability;

/// <summary>
/// Cho Frontend biết người dùng này, trong Chapter/Scene này, chạy được nhiệm vụ AI nào và còn thiếu điều kiện gì.
/// Dùng đúng <see cref="AiWorkflowFacts"/> như RunWorkflow nên "chạy được" ở đây khớp với việc Backend sẽ chặn hay không.
/// Chỉ để hiển thị: quyền thật vẫn do RunWorkflow kiểm lại ở thời điểm chạy.
/// </summary>
public sealed record GetWorkflowAvailabilityQuery(Guid SeriesId, Guid UserId, Guid ChapterId, Guid? SceneId)
    : IRequest<Result<AiWorkflowAvailabilityResponse>>;

public sealed class GetWorkflowAvailabilityQueryHandler
    : IRequestHandler<GetWorkflowAvailabilityQuery, Result<AiWorkflowAvailabilityResponse>>
{
    private readonly IPanelForgeDbContext _db;
    private readonly IWorkspaceAuthorizationService _auth;

    public GetWorkflowAvailabilityQueryHandler(IPanelForgeDbContext db, IWorkspaceAuthorizationService auth)
    {
        _db = db;
        _auth = auth;
    }

    public async Task<Result<AiWorkflowAvailabilityResponse>> Handle(GetWorkflowAvailabilityQuery q, CancellationToken ct)
    {
        var workspaceId = await AiRunHelpers.WorkspaceOfSeriesAsync(_db, q.SeriesId, ct);
        if (workspaceId is null)
            return AiRunHelpers.Fail<AiWorkflowAvailabilityResponse>("Series không tồn tại.", ResultErrorCodes.NotFound);
        if (!await _auth.IsMemberAsync(q.UserId, workspaceId.Value, ct))
            return AiRunHelpers.Fail<AiWorkflowAvailabilityResponse>("Bạn không phải thành viên của Workspace này.", ResultErrorCodes.Forbidden);

        var chapter = await _db.Chapters.AsNoTracking().FirstOrDefaultAsync(c => c.Id == q.ChapterId && c.SeriesId == q.SeriesId, ct);
        if (chapter is null)
            return AiRunHelpers.Fail<AiWorkflowAvailabilityResponse>("Chapter không thuộc Series.", ResultErrorCodes.NotFound);
        Domain.Entities.Content.Scene? scene = null;
        if (q.SceneId is { } sceneId)
        {
            scene = await _db.Scenes.AsNoTracking().FirstOrDefaultAsync(s => s.Id == sceneId && s.ChapterId == chapter.Id, ct);
            if (scene is null)
                return AiRunHelpers.Fail<AiWorkflowAvailabilityResponse>("Scene không thuộc Chapter.", ResultErrorCodes.NotFound);
        }

        var facts = await AiWorkflowFacts.LoadAsync(_db, _auth, q.UserId, workspaceId.Value, chapter, scene, ct);
        var workflows = new List<AiWorkflowAvailabilityDto>();
        foreach (var type in Enum.GetValues<AiWorkflowType>())
        {
            var missing = facts.Evaluate(type).ToList();
            if (AiWorkflowPolicy.NeedsScene(type) && scene is null)
                missing.Add(new AiMissingConditionDto("SCENE_REQUIRED", "Context", "Chọn một Scene để chạy nhiệm vụ này."));
            workflows.Add(new AiWorkflowAvailabilityDto(
                type, AiWorkflowPolicy.Title(type), AiWorkflowPolicy.Description(type),
                WorkflowRules.AgentsOf(type.ToString()), AiWorkflowPolicy.RunRoles(type), AiWorkflowPolicy.ResultKinds(type),
                missing.Count == 0, missing));
        }
        return Result<AiWorkflowAvailabilityResponse>.Success(
            new AiWorkflowAvailabilityResponse(facts.Role, chapter.Id, scene?.Id, workflows));
    }
}
