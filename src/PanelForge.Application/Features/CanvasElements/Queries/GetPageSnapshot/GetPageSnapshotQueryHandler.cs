using MediatR;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common;
using PanelForge.Application.Features.CanvasElements.Common;

namespace PanelForge.Application.Features.CanvasElements.Queries.GetPageSnapshot;

public sealed class GetPageSnapshotQueryHandler
    : IRequestHandler<GetPageSnapshotQuery, Result<PageSnapshotDto>>
{
    private readonly IPageRepository _pageRepository;
    private readonly IPanelForgeDbContext _db;

    public GetPageSnapshotQueryHandler(IPageRepository pageRepository, IPanelForgeDbContext db)
    {
        _pageRepository = pageRepository;
        _db = db;
    }

    public async Task<Result<PageSnapshotDto>> Handle(
        GetPageSnapshotQuery query,
        CancellationToken cancellationToken)
    {
        var page = await _pageRepository.GetAsync(query.PageId, cancellationToken);
        if (page is null)
            return Result<PageSnapshotDto>.Failure(
                $"Page {query.PageId} không tồn tại.");

        // BR-05 (chữ lỗi thời) + BR-20 (cảnh báo tràn chữ, thứ tự đọc, đè Key Artwork Region): một lượt cho cả Page
        var analysis = await LetteringPageAnalysis.AnalyzeAsync(_db, page, cancellationToken);

        var elements = page.Elements.Values
            .Where(e => query.IncludeRemoved || !e.IsRemoved)
            .OrderBy(e => e.ZIndex)
            .Select(e =>
            {
                LineFacts? line = null;
                if (!e.IsRemoved && e.ScriptLineId is { } id) analysis.Lines.TryGetValue(id, out line);

                return new ElementSnapshotDto(
                    ElementId:       e.ElementId,
                    ElementType:     e.ElementType,
                    ZIndex:          e.ZIndex,
                    X:               e.X,
                    Y:               e.Y,
                    Width:           e.Width,
                    Height:          e.Height,
                    Content:         e.Content,
                    AssetId:         e.AssetId,
                    IsRemoved:       e.IsRemoved,
                    ScriptLineId:    e.ScriptLineId,
                    SpeakerId:       e.SpeakerId,
                    RenderedText:    e.RenderedText,
                    CurrentLineText: line?.Text,
                    IsStale:         analysis.StaleElementIds.Contains(e.ElementId),
                    ReadingOrder:    e.ReadingOrder,
                    OverflowAcknowledgedReason: e.OverflowAckReason,
                    Version:         e.Version,
                    PanelId:         e.PanelId,
                    Rotation:        e.Rotation,
                    BibleEntryId:    e.BibleEntryId
                );
            })
            .ToList();

        return Result<PageSnapshotDto>.Success(new PageSnapshotDto(
            PageId:     page.Id,
            Version:    page.Version,
            PageNumber: page.PageNumber,
            Elements:   elements,
            Warnings:   analysis.Warnings
        ));
    }
}
