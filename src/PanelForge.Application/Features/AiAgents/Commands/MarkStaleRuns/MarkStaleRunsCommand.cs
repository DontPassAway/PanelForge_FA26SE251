using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.AiAgents.Models;
using PanelForge.Domain.Entities.Workspaces;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.AiAgents.Commands.MarkStaleRuns;

/// <summary>
/// Cơ chế xác định cho run dở khi AI service chết/khởi động lại: run Queued/Running không có hoạt động quá
/// RunStaleAfterSeconds → Interrupted. Không có resume: người dùng chạy lại workflow (các step đã lưu vẫn xem được).
/// </summary>
public sealed record MarkStaleRunsCommand : IRequest<int>;

public sealed class MarkStaleRunsCommandHandler : IRequestHandler<MarkStaleRunsCommand, int>
{
    private readonly IPanelForgeDbContext _db;
    private readonly AiAgentSettings _settings;

    public MarkStaleRunsCommandHandler(IPanelForgeDbContext db, AiAgentSettings settings)
    {
        _db = db;
        _settings = settings;
    }

    public async Task<int> Handle(MarkStaleRunsCommand request, CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow.AddSeconds(-_settings.RunStaleAfterSeconds);
        var stale = await _db.AiWorkflowRuns
            .Where(r => (r.Status == AiRunStatus.Queued || r.Status == AiRunStatus.Running) && r.LastActivityAtUtc < cutoff)
            .ToListAsync(ct);
        foreach (var run in stale)
            run.MarkInterrupted($"AI service không còn báo hoạt động quá {_settings.RunStaleAfterSeconds}s (đã dừng hoặc khởi động lại). Chạy lại workflow.");

        // OPS-03: trả token đã giữ chỗ mà run chưa kịp chốt, trong cùng lần lưu (xung đột xmin → lần quét sau làm lại).
        var held = stale.Where(r => r.ReservedTokens > 0).ToList();
        if (held.Count > 0)
        {
            var workspaceIds = held.Select(r => r.WorkspaceId).Distinct().ToList();
            var configs = await _db.WorkspaceAiConfigs.Where(c => workspaceIds.Contains(c.WorkspaceId)).ToListAsync(ct);
            foreach (var run in held)
            {
                var tokens = run.TakeReservation();
                var config = configs.FirstOrDefault(c => c.WorkspaceId == run.WorkspaceId);
                if (config is not null && WorkspaceAiConfig.IsCountedThisMonth(config.LastResetAt, config.CreatedAt, DateTime.UtcNow))
                    config.SettleReservedTokens(tokens, 0);
            }
        }
        if (stale.Count > 0) await _db.SaveChangesAsync(ct);
        return stale.Count;
    }
}