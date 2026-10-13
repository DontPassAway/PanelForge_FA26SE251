using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Exports.Models;

namespace PanelForge.Application.Features.Exports.Queries;

/// <summary>UC-12 / UC-14: các bản xuất của chương, mới nhất trước (FE hỏi lại để xem trạng thái).</summary>
public sealed record GetExportJobsQuery(Guid ChapterId) : IRequest<Result<IReadOnlyList<ExportJobDto>>>;

public sealed class GetExportJobsQueryHandler : IRequestHandler<GetExportJobsQuery, Result<IReadOnlyList<ExportJobDto>>>
{
    private readonly IPanelForgeDbContext _db;

    public GetExportJobsQueryHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<IReadOnlyList<ExportJobDto>>> Handle(GetExportJobsQuery q, CancellationToken ct)
    {
        var jobs = await _db.ExportJobs.AsNoTracking().Where(j => j.ChapterId == q.ChapterId)
            .OrderByDescending(j => j.CreatedAt).ToListAsync(ct);
        return Result<IReadOnlyList<ExportJobDto>>.Success(jobs.Select(ExportJobDto.From).ToList());
    }
}
