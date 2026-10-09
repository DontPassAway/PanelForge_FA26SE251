using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Consistency.Models;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Consistency.Commands;

/// <summary>
/// CF3 bước 5 (BR-21): Editor/Producer phân loại một finding đang mở: Confirmed, Rejected, Ignored (bắt buộc lý do)
/// hoặc NeedsClarification (bắt buộc câu hỏi). Chỉ người, không phải AI hay code, kết luận.
/// </summary>
public sealed record TriageConsistencyFindingCommand(
    Guid ChapterId, Guid FindingId, Guid UserId, ConsistencyFindingStatus Decision, string? Note)
    : IRequest<Result<ConsistencyFindingDto>>;

public sealed class TriageConsistencyFindingCommandHandler
    : IRequestHandler<TriageConsistencyFindingCommand, Result<ConsistencyFindingDto>>
{
    private readonly IPanelForgeDbContext _db;

    public TriageConsistencyFindingCommandHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<ConsistencyFindingDto>> Handle(TriageConsistencyFindingCommand cmd, CancellationToken ct)
    {
        var finding = await _db.ConsistencyFindings
            .FirstOrDefaultAsync(f => f.Id == cmd.FindingId && f.ChapterId == cmd.ChapterId, ct);
        if (finding is null)
            return Result<ConsistencyFindingDto>.Failure("Finding không tồn tại trong Chapter này.", ResultErrorCodes.NotFound);

        try
        {
            finding.Triage(cmd.Decision, cmd.Note, cmd.UserId);
        }
        catch (ArgumentException ex)
        {
            return Result<ConsistencyFindingDto>.Failure(ex.Message, ResultErrorCodes.Validation);
        }
        catch (InvalidOperationException ex)
        {
            return Result<ConsistencyFindingDto>.Failure(ex.Message, ResultErrorCodes.Conflict);
        }

        finding.UpdatedBy = cmd.UserId.ToString();
        await _db.SaveChangesAsync(ct);
        return Result<ConsistencyFindingDto>.Success(ConsistencyFindingDto.From(finding));
    }
}

/// <summary>Danh sách finding của Chapter (thành viên Workspace), lọc tuỳ chọn theo trạng thái; lỗi mở và nặng trước.</summary>
public sealed record GetConsistencyFindingsQuery(Guid ChapterId, ConsistencyFindingStatus? Status = null)
    : IRequest<Result<IReadOnlyList<ConsistencyFindingDto>>>;

public sealed class GetConsistencyFindingsQueryHandler
    : IRequestHandler<GetConsistencyFindingsQuery, Result<IReadOnlyList<ConsistencyFindingDto>>>
{
    private readonly IPanelForgeDbContext _db;

    public GetConsistencyFindingsQueryHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<IReadOnlyList<ConsistencyFindingDto>>> Handle(GetConsistencyFindingsQuery q, CancellationToken ct)
    {
        if (!await _db.Chapters.AsNoTracking().AnyAsync(c => c.Id == q.ChapterId, ct))
            return Result<IReadOnlyList<ConsistencyFindingDto>>.Failure($"Chapter {q.ChapterId} không tồn tại.", ResultErrorCodes.NotFound);

        var query = _db.ConsistencyFindings.AsNoTracking().Where(f => f.ChapterId == q.ChapterId);
        if (q.Status is { } status) query = query.Where(f => f.Status == status);

        var findings = await query.ToListAsync(ct);
        return Result<IReadOnlyList<ConsistencyFindingDto>>.Success(
            ConsistencyFindingOrdering.Order(findings).Select(ConsistencyFindingDto.From).ToList());
    }
}
