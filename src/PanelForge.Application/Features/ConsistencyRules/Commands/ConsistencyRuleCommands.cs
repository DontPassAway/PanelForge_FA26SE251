using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Interfaces.Persistence;
using PanelForge.Domain.Entities.Content;

namespace PanelForge.Application.Features.ConsistencyRules.Commands;

// ─── DTO ──────────────────────────────────────────────────────────────────────

public sealed record ConsistencyRuleDto(
    Guid Id,
    Guid SeriesId,
    string Name,
    string RuleType,
    string? Description,
    bool IsEnabled,
    string? Pattern,
    string Severity,
    string ConfigurationJson,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

public static class ConsistencyRuleExtensions
{
    public static ConsistencyRuleDto ToDto(this ConsistencyRule r) => new(
        Id: r.Id,
        SeriesId: r.SeriesId,
        Name: r.Name,
        RuleType: r.RuleType,
        Description: r.Description,
        IsEnabled: r.IsEnabled,
        Pattern: r.Pattern,
        Severity: r.Severity,
        ConfigurationJson: r.ConfigurationJson,
        CreatedAt: r.CreatedAt,
        UpdatedAt: r.UpdatedAt);
}

// ─── Create ───────────────────────────────────────────────────────────────────

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

// ─── Update ───────────────────────────────────────────────────────────────────

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

// ─── Delete ───────────────────────────────────────────────────────────────────

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
