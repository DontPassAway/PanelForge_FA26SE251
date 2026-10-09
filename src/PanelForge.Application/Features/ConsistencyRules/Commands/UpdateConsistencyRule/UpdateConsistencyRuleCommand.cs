using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Domain.Entities.Content;
using PanelForge.Application.Features.ConsistencyRules.Models;
using PanelForge.Application.Features.ConsistencyRules.Common;

namespace PanelForge.Application.Features.ConsistencyRules.Commands.UpdateConsistencyRule;

public sealed record UpdateConsistencyRuleCommand(
    Guid RuleId,
    Guid SeriesId,
    Guid RequestingUserId,
    string Name,
    string RuleType,
    string? Description,
    bool IsEnabled,
    string? Pattern,
    string Severity,
    string? ConfigurationJson
) : IRequest<Result<ConsistencyRuleDto>>;

public sealed class UpdateConsistencyRuleCommandHandler
    : IRequestHandler<UpdateConsistencyRuleCommand, Result<ConsistencyRuleDto>>
{
    private readonly IPanelForgeDbContext _dbContext;
    public UpdateConsistencyRuleCommandHandler(IPanelForgeDbContext dbContext) => _dbContext = dbContext;

    public async Task<Result<ConsistencyRuleDto>> Handle(
        UpdateConsistencyRuleCommand cmd, CancellationToken ct)
    {
        var series = await _dbContext.Series.FirstOrDefaultAsync(s => s.Id == cmd.SeriesId, ct);
        if (series is null) return Result<ConsistencyRuleDto>.Failure("Series không tồn tại.");

        var isMember = await _dbContext.WorkspaceMembers
            .AnyAsync(m => m.WorkspaceId == series.WorkspaceId && m.UserId == cmd.RequestingUserId, ct);
        if (!isMember) return Result<ConsistencyRuleDto>.Failure("Bạn không có quyền truy cập Series này.");

        var rule = await _dbContext.ConsistencyRules
            .FirstOrDefaultAsync(r => r.Id == cmd.RuleId && r.SeriesId == cmd.SeriesId, ct);
        if (rule is null) return Result<ConsistencyRuleDto>.Failure("ConsistencyRule không tồn tại.");

        try { rule.Update(cmd.Name, cmd.RuleType, cmd.Description, cmd.IsEnabled, cmd.Pattern, cmd.Severity, cmd.ConfigurationJson); }
        catch (ArgumentException ex) { return Result<ConsistencyRuleDto>.Failure(ex.Message); }

        await _dbContext.SaveChangesAsync(ct);
        return Result<ConsistencyRuleDto>.Success(rule.ToDto());
    }
}
