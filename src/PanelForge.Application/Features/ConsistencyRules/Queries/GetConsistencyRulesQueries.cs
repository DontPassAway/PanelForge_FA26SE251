using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.ConsistencyRules.Commands;
using PanelForge.Application.Interfaces.Persistence;

namespace PanelForge.Application.Features.ConsistencyRules.Queries;

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

public sealed record GetConsistencyRuleByIdQuery(Guid RuleId, Guid SeriesId)
    : IRequest<Result<ConsistencyRuleDto>>;

public sealed class GetConsistencyRuleByIdQueryHandler
    : IRequestHandler<GetConsistencyRuleByIdQuery, Result<ConsistencyRuleDto>>
{
    private readonly IPanelForgeDbContext _dbContext;
    public GetConsistencyRuleByIdQueryHandler(IPanelForgeDbContext dbContext) => _dbContext = dbContext;

    public async Task<Result<ConsistencyRuleDto>> Handle(
        GetConsistencyRuleByIdQuery query, CancellationToken ct)
    {
        var rule = await _dbContext.ConsistencyRules
            .FirstOrDefaultAsync(r => r.Id == query.RuleId && r.SeriesId == query.SeriesId, ct);

        if (rule is null)
            return Result<ConsistencyRuleDto>.Failure("ConsistencyRule không tồn tại.");

        return Result<ConsistencyRuleDto>.Success(rule.ToDto());
    }
}
