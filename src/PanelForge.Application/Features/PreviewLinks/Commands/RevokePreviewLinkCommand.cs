using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;

namespace PanelForge.Application.Features.PreviewLinks.Commands;

/// <summary>BR-17: Producer thu hồi link trước hạn. Thu hồi lại link đã thu hồi = không đổi.</summary>
public sealed record RevokePreviewLinkCommand(Guid ChapterId, Guid LinkId, Guid UserId) : IRequest<Result<bool>>;

public sealed class RevokePreviewLinkCommandHandler : IRequestHandler<RevokePreviewLinkCommand, Result<bool>>
{
    private readonly IPanelForgeDbContext _db;

    public RevokePreviewLinkCommandHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<bool>> Handle(RevokePreviewLinkCommand cmd, CancellationToken ct)
    {
        var link = await _db.PreviewLinks.FirstOrDefaultAsync(l => l.Id == cmd.LinkId && l.ChapterId == cmd.ChapterId, ct);
        if (link is null) return Result<bool>.Failure("Link xem thử không tồn tại.", ResultErrorCodes.NotFound);

        link.Revoke(cmd.UserId, "Producer thu hồi");
        await _db.SaveChangesAsync(ct);
        return Result<bool>.Success(true);
    }
}
