using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.ConsistencyRules.Models;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.ConsistencyRules.Common;

namespace PanelForge.Application.Features.ConsistencyRules.Queries.GetConsistencyRuleById;

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
