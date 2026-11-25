using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Chapters.Common;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Chapters.Queries.GetChapterLockGate;

/// <summary>Producer Lock-Gate Check: liệt kê MỌI điều kiện chưa đạt trước khi duyệt/khoá chương (tất định, không LLM).</summary>
public sealed record GetChapterLockGateQuery(Guid ChapterId) : IRequest<Result<ChapterLockGateDto>>;

public sealed class GetChapterLockGateQueryHandler : IRequestHandler<GetChapterLockGateQuery, Result<ChapterLockGateDto>>
{
    private readonly IPanelForgeDbContext _db;

    public GetChapterLockGateQueryHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<ChapterLockGateDto>> Handle(GetChapterLockGateQuery q, CancellationToken ct)
    {
        var chapter = await _db.Chapters.AsNoTracking().FirstOrDefaultAsync(c => c.Id == q.ChapterId, ct);
        if (chapter is null)
            return Result<ChapterLockGateDto>.Failure($"Chương {q.ChapterId} không tồn tại.", ResultErrorCodes.NotFound);

        var items = await ChapterLockGate.EvaluateAsync(_db, chapter, ct);
        if (chapter.Status != ChapterStatus.InProduction)
            items.Insert(0, new ChapterLockGateItem("NOT_IN_PRODUCTION", true, 0, $"Chapter đang ở trạng thái {chapter.Status}."));
        return Result<ChapterLockGateDto>.Success(
            new ChapterLockGateDto(chapter.Id, chapter.Status, !items.Any(i => i.Blocking), items));
    }
}
