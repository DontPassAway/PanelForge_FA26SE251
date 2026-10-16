using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Bible.Models;
using PanelForge.Application.Features.Bible.Queries.GetBibleEntriesAtChapter;

namespace PanelForge.Application.Features.Bible.Queries.GetPanelBible;

/// <summary>
/// UC-07 bước 1, FR-Artist-04: các mục Bible gắn với một Panel (nhân vật nói trong các ScriptLine neo vào Panel),
/// đúng bản có hiệu lực ở chương của Panel (BR-15) và chỉ bản canon đã khoá, giống nguồn AI dùng khi kiểm tra (CF3).
/// </summary>
public sealed record GetPanelBibleQuery(Guid PanelId) : IRequest<Result<IReadOnlyList<BibleEntryDto>>>;

public sealed class GetPanelBibleQueryHandler : IRequestHandler<GetPanelBibleQuery, Result<IReadOnlyList<BibleEntryDto>>>
{
    private readonly IPanelForgeDbContext _dbContext;
    private readonly ISender _sender;

    public GetPanelBibleQueryHandler(IPanelForgeDbContext dbContext, ISender sender)
    {
        _dbContext = dbContext;
        _sender = sender;
    }

    public async Task<Result<IReadOnlyList<BibleEntryDto>>> Handle(GetPanelBibleQuery request, CancellationToken cancellationToken)
    {
        var chapter = await _dbContext.Panels
            .Where(p => p.Id == request.PanelId)
            .Select(p => new { p.Page.Chapter.SeriesId, p.Page.Chapter.ChapterNumber })
            .FirstOrDefaultAsync(cancellationToken);
        if (chapter is null)
            return Result<IReadOnlyList<BibleEntryDto>>.Failure($"Panel {request.PanelId} không tồn tại.", ResultErrorCodes.NotFound);

        var entryIds = await _dbContext.ScriptLines
            .Where(l => l.PanelId == request.PanelId && l.SpeakerCharacterId != null)
            .Select(l => l.SpeakerCharacterId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);
        if (entryIds.Count == 0)
            return Result<IReadOnlyList<BibleEntryDto>>.Success([]);

        // Chương lẻ (vd. 12.5) dùng Bible của chương nguyên trước nó, giống AiSnapshotBuilder
        var chapterNumber = (int)Math.Floor(chapter.ChapterNumber);
        return await _sender.Send(
            new GetBibleEntriesAtChapterQuery(chapter.SeriesId, chapterNumber, CanonOnly: true, EntryIds: entryIds),
            cancellationToken);
    }
}
