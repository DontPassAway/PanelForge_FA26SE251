using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Domain.Entities.Content;
using PanelForge.Application.Features.ConsistencyRules.Models;
using PanelForge.Application.Features.ConsistencyRules.Common;

namespace PanelForge.Application.Features.ConsistencyRules.Commands.CreateConsistencyRule;

public sealed record CreateConsistencyRuleCommand(
    Guid SeriesId,
    Guid RequestingUserId,
    string Name,
    string RuleType,
    string? Description = null,
    bool IsEnabled = true,
    string? Pattern = null,
    string Severity = "Warning",
    string? ConfigurationJson = null
) : IRequest<Result<ConsistencyRuleDto>>;

public sealed class CreateConsistencyRuleCommandHandler
    : IRequestHandler<CreateConsistencyRuleCommand, Result<ConsistencyRuleDto>>
{
    private readonly IPanelForgeDbContext _dbContext;
    public CreateConsistencyRuleCommandHandler(IPanelForgeDbContext dbContext) => _dbContext = dbContext;

    public async Task<Result<ConsistencyRuleDto>> Handle(
        CreateConsistencyRuleCommand cmd, CancellationToken ct)
    {
        var series = await _dbContext.Series.FirstOrDefaultAsync(s => s.Id == cmd.SeriesId, ct);
        if (series is null) return Result<ConsistencyRuleDto>.Failure("Series không tồn tại.");

        var isMember = await _dbContext.WorkspaceMembers
            .AnyAsync(m => m.WorkspaceId == series.WorkspaceId && m.UserId == cmd.RequestingUserId, ct);
        if (!isMember) return Result<ConsistencyRuleDto>.Failure("Bạn không có quyền truy cập Series này.");

        ConsistencyRule rule;
        try
        {
            rule = ConsistencyRule.Create(cmd.SeriesId, cmd.Name, cmd.RuleType,
                cmd.Description, cmd.IsEnabled, cmd.Pattern, cmd.Severity, cmd.ConfigurationJson);
        }
        catch (ArgumentException ex)
        {
            return Result<ConsistencyRuleDto>.Failure(ex.Message);
        }

        _dbContext.ConsistencyRules.Add(rule);
        await _dbContext.SaveChangesAsync(ct);
        return Result<ConsistencyRuleDto>.Success(rule.ToDto());
    }
}
