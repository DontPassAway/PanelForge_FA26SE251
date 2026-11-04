using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;

namespace PanelForge.Application.Features.Pages.Queries.SplitArtwork;

/// <summary>
/// Gợi ý tách panel + bong bóng từ ảnh vẽ cả trang. Chỉ trả toạ độ, không lưu gì.
/// Quyền Workspace/Series (Producer, Artist) kiểm ở controller qua RequireWorkspaceRole theo pageId.
/// </summary>
public sealed record SplitArtworkQuery(Guid PageId, Stream ImageStream, string FileName, bool RightToLeft)
    : IRequest<Result<PanelSplitResult>>;

public sealed class SplitArtworkQueryHandler : IRequestHandler<SplitArtworkQuery, Result<PanelSplitResult>>
{
    private readonly IPanelForgeDbContext _db;
    private readonly IPanelSplitter _splitter;

    public SplitArtworkQueryHandler(IPanelForgeDbContext db, IPanelSplitter splitter)
    {
        _db = db;
        _splitter = splitter;
    }

    public async Task<Result<PanelSplitResult>> Handle(SplitArtworkQuery query, CancellationToken ct)
    {
        // Bảng quan hệ đủ để biết Page tồn tại; không dựng lại event stream Marten chỉ để kiểm
        if (!await _db.Pages.AsNoTracking().AnyAsync(p => p.Id == query.PageId, ct))
            return Result<PanelSplitResult>.Failure($"Page {query.PageId} không tồn tại.", ResultErrorCodes.NotFound);

        try
        {
            return Result<PanelSplitResult>.Success(
                await _splitter.SplitAsync(query.ImageStream, query.FileName, query.RightToLeft, ct));
        }
        catch (InvalidImageException ex)
        {
            // Chỉ lỗi đầu vào; lỗi native thật nổi lên middleware (500) và được log
            return Result<PanelSplitResult>.Failure(ex.Message, ResultErrorCodes.Validation);
        }
    }
}
