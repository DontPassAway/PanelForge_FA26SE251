using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.AiAgents.Common;
using PanelForge.Application.Features.AiAgents.Models;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.AiAgents.Queries.GetSuggestions;

public sealed record GetSuggestionsQuery(Guid SeriesId, Guid UserId, AiSuggestionStatus? Status, Guid? RunId)
    : IRequest<Result<IReadOnlyList<AiSuggestionDto>>>;

public sealed class GetSuggestionsQueryHandler : IRequestHandler<GetSuggestionsQuery, Result<IReadOnlyList<AiSuggestionDto>>>
{
    private readonly IPanelForgeDbContext _db;
    private readonly IWorkspaceAuthorizationService _auth;

    public GetSuggestionsQueryHandler(IPanelForgeDbContext db, IWorkspaceAuthorizationService auth)
    {
        _db = db;
        _auth = auth;
    }

    public async Task<Result<IReadOnlyList<AiSuggestionDto>>> Handle(GetSuggestionsQuery q, CancellationToken ct)
    {
        var workspaceId = await AiRunHelpers.WorkspaceOfSeriesAsync(_db, q.SeriesId, ct);
        if (workspaceId is null)
            return AiRunHelpers.Fail<IReadOnlyList<AiSuggestionDto>>("Series không tồn tại.", ResultErrorCodes.NotFound);
        var viewerRole = await _auth.GetMemberRoleAsync(q.UserId, workspaceId.Value, ct);
        if (viewerRole is null)
            return AiRunHelpers.Fail<IReadOnlyList<AiSuggestionDto>>("Bạn không phải thành viên của Workspace này.", ResultErrorCodes.Forbidden);

        var query = _db.AiSuggestions.AsNoTracking().Where(s => s.SeriesId == q.SeriesId);
        if (q.Status is { } status) query = query.Where(s => s.Status == status);
        if (q.RunId is { } runId) query = query.Where(s => s.RunId == runId);

        var list = await query.OrderByDescending(s => s.CreatedAt).Take(200).ToListAsync(ct);
        var ids = list.Select(s => s.Id).ToList();
        var decisions = await _db.AiSuggestionDecisions.AsNoTracking().Where(d => ids.Contains(d.SuggestionId)).ToListAsync(ct);

        return Result<IReadOnlyList<AiSuggestionDto>>.Success(
            list.Select(s => AiRunHelpers.ToDto(s, decisions.Where(d => d.SuggestionId == s.Id), viewerRole)).ToList());
    }
}