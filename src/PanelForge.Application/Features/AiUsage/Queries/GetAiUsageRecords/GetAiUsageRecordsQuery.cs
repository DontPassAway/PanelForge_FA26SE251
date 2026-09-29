using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Domain.Enums;
using PanelForge.Application.Features.AiUsage.Models;
using PanelForge.Application.Features.AiUsage.Common;

namespace PanelForge.Application.Features.AiUsage.Queries.GetAiUsageRecords;

public sealed record GetAiUsageRecordsQuery(
    Guid? WorkspaceId = null,
    Guid? UserId = null,
    string? Feature = null,
    AiUsageStatus? Status = null,
    DateTime? From = null,
    DateTime? To = null,
    int Page = 1,
    int PageSize = 30
) : IRequest<Result<AiUsageRecordsPagedResponse>>;

public sealed class GetAiUsageRecordsQueryHandler
    : IRequestHandler<GetAiUsageRecordsQuery, Result<AiUsageRecordsPagedResponse>>
{
    private readonly IPanelForgeDbContext _dbContext;
    public GetAiUsageRecordsQueryHandler(IPanelForgeDbContext dbContext) => _dbContext = dbContext;

    public async Task<Result<AiUsageRecordsPagedResponse>> Handle(GetAiUsageRecordsQuery q, CancellationToken ct)
    {
        if (q.From.HasValue && q.To.HasValue && q.From.Value >= q.To.Value)
            return Result<AiUsageRecordsPagedResponse>.Failure("Khoảng thời gian không hợp lệ: 'from' phải nhỏ hơn 'to'.");

        var page = Math.Max(1, q.Page);
        var pageSize = Math.Clamp(q.PageSize, 1, 100);
        var query = _dbContext.AiUsageRecords.AsNoTracking().ApplyFilter(q);

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(r => r.OccurredAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(AiUsageFiltering.ToDto)
            .ToListAsync(ct);

        return Result<AiUsageRecordsPagedResponse>.Success(new AiUsageRecordsPagedResponse(total, page, pageSize, items));
    }
}
