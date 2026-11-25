using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Users.Library;

/// <summary>
/// Thêm (<see cref="Add"/> = true) hoặc bỏ một bộ truyện khỏi danh sách Đọc sau / Theo dõi / Yêu thích.
/// Idempotent: thêm khi đã có hoặc bỏ khi không có đều thành công. Với Theo dõi, thêm khi đã có sẽ cập nhật mốc đã xem.
/// </summary>
public sealed record SetLibraryItemCommand(Guid UserId, ReaderLibraryList List, Guid SeriesId, bool Add, DateTime? LastSeenAt = null)
    : IRequest<Result<bool>>;

public sealed class SetLibraryItemCommandHandler : IRequestHandler<SetLibraryItemCommand, Result<bool>>
{
    private readonly IPanelForgeDbContext _db;

    public SetLibraryItemCommandHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<bool>> Handle(SetLibraryItemCommand cmd, CancellationToken ct)
    {
        var existing = await _db.ReaderLibraryItems
            .FirstOrDefaultAsync(i => i.UserId == cmd.UserId && i.List == cmd.List && i.SeriesId == cmd.SeriesId, ct);

        if (!cmd.Add)
        {
            if (existing is not null)
            {
                _db.ReaderLibraryItems.Remove(existing);
                await _db.SaveChangesAsync(ct);
            }
            return Result<bool>.Success(false);
        }

        if (existing is not null)
        {
            existing.MarkSeen(cmd.LastSeenAt);
        }
        else
        {
            var published = await ReaderLibrary.PublishedSeriesAsync(_db, [cmd.SeriesId], ct);
            if (published.Count == 0)
                return Result<bool>.Failure("Không tìm thấy bộ truyện đã phát hành.", ResultErrorCodes.NotFound);
            _db.ReaderLibraryItems.Add(ReaderLibraryItem.Create(cmd.UserId, cmd.SeriesId, cmd.List, cmd.LastSeenAt));
        }
        await _db.SaveChangesAsync(ct);
        return Result<bool>.Success(true);
    }
}
