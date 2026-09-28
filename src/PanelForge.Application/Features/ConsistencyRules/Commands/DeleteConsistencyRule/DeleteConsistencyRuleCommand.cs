using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Domain.Entities.Content;

namespace PanelForge.Application.Features.ConsistencyRules.Commands.DeleteConsistencyRule;

public sealed record DeleteConsistencyRuleCommand(Guid RuleId, Guid SeriesId, Guid RequestingUserId)
    : IRequest<Result<string>>;

public sealed class DeleteConsistencyRuleCommandHandler
    : IRequestHandler<DeleteConsistencyRuleCommand, Result<string>>
{
    private readonly IPanelForgeDbContext _dbContext;
    public DeleteConsistencyRuleCommandHandler(IPanelForgeDbContext dbContext) => _dbContext = dbContext;

    public async Task<Result<string>> Handle(DeleteConsistencyRuleCommand cmd, CancellationToken ct)
    {
        var series = await _dbContext.Series.FirstOrDefaultAsync(s => s.Id == cmd.SeriesId, ct);
        if (series is null) return Result<string>.Failure("Series không tồn tại.");

        var isMember = await _dbContext.WorkspaceMembers
            .AnyAsync(m => m.WorkspaceId == series.WorkspaceId && m.UserId == cmd.RequestingUserId, ct);
        if (!isMember) return Result<string>.Failure("Bạn không có quyền truy cập Series này.");

        var rule = await _dbContext.ConsistencyRules
            .FirstOrDefaultAsync(r => r.Id == cmd.RuleId && r.SeriesId == cmd.SeriesId, ct);
        if (rule is null) return Result<string>.Failure("ConsistencyRule không tồn tại.");

        _dbContext.ConsistencyRules.Remove(rule);
        await _dbContext.SaveChangesAsync(ct);
        return Result<string>.Success("ConsistencyRule đã được xóa.");
    }
}
