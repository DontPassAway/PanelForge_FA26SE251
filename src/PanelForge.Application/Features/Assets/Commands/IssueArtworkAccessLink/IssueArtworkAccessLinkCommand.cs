using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.Assets.Models;

namespace PanelForge.Application.Features.Assets.Commands.IssueArtworkAccessLink;

/// <summary>BR-16: artwork chưa phát hành không có URL công khai; thành viên xin link ngắn hạn gắn với chính mình.</summary>
public sealed record IssueArtworkAccessLinkCommand(Guid ChapterId, Guid AssetReferenceId, Guid UserId)
    : IRequest<Result<ArtworkAccessLinkDto>>;

public sealed class IssueArtworkAccessLinkCommandHandler
    : IRequestHandler<IssueArtworkAccessLinkCommand, Result<ArtworkAccessLinkDto>>
{
    public static readonly TimeSpan LinkLifetime = TimeSpan.FromMinutes(5);

    private readonly IPanelForgeDbContext _db;
    private readonly IAssetLinkSigner _signer;

    public IssueArtworkAccessLinkCommandHandler(IPanelForgeDbContext db, IAssetLinkSigner signer)
    {
        _db = db;
        _signer = signer;
    }

    public async Task<Result<ArtworkAccessLinkDto>> Handle(IssueArtworkAccessLinkCommand cmd, CancellationToken ct)
    {
        // Lọc theo ChapterId của route để chặn IDOR giữa các Workspace
        if (!await _db.AssetReferences.AsNoTracking()
                .AnyAsync(r => r.Id == cmd.AssetReferenceId && r.ChapterId == cmd.ChapterId, ct))
            return Result<ArtworkAccessLinkDto>.Failure($"Artwork {cmd.AssetReferenceId} không tồn tại.", ResultErrorCodes.NotFound);

        var token = _signer.Create(cmd.AssetReferenceId, cmd.UserId, LinkLifetime);
        return Result<ArtworkAccessLinkDto>.Success(new ArtworkAccessLinkDto(
            $"/api/assets/download?token={Uri.EscapeDataString(token)}", DateTime.UtcNow.Add(LinkLifetime)));
    }
}
