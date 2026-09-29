using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.ConsistencyRules.Models;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.ConsistencyRules.Common;

namespace PanelForge.Application.Features.ConsistencyRules.Queries.GetConsistencyRules;

public sealed record GetConsistencyRulesQuery(Guid SeriesId)
    : IRequest<Result<IReadOnlyList<ConsistencyRuleDto>>>;

public sealed class GetConsistencyRulesQueryHandler
    : IRequestHandler<GetConsistencyRulesQuery, Result<IReadOnlyList<ConsistencyRuleDto>>>
{
    private readonly IPanelForgeDbContext _dbContext;
    public GetConsistencyRulesQueryHandler(IPanelForgeDbContext dbContext) => _dbContext = dbContext;

    public async Task<Result<IReadOnlyList<ConsistencyRuleDto>>> Handle(
        GetConsistencyRulesQuery query, CancellationToken ct)
    {
        var rules = await _dbContext.ConsistencyRules
            .Where(r => r.SeriesId == query.SeriesId)
            .OrderBy(r => r.RuleType).ThenBy(r => r.Name)
            .ToListAsync(ct);

        return Result<IReadOnlyList<ConsistencyRuleDto>>.Success(
            rules.Select(r => r.ToDto()).ToList());
    }
}
