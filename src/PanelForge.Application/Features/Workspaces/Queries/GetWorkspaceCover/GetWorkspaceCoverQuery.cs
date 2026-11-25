using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.Assets.Common;
using PanelForge.Application.Features.Workspaces.Commands.SetWorkspaceCover;

namespace PanelForge.Application.Features.Workspaces.Queries.GetWorkspaceCover;

/// <summary>Thành viên Workspace xem ảnh bìa hiện tại của Studio (quyền kiểm ở route theo workspaceId).</summary>
public sealed record GetWorkspaceCoverQuery(Guid WorkspaceId) : IRequest<Result<WorkspaceCoverContent>>;

public sealed record WorkspaceCoverContent(byte[] Content, string ContentType);

public sealed class GetWorkspaceCoverQueryHandler : IRequestHandler<GetWorkspaceCoverQuery, Result<WorkspaceCoverContent>>
{
    private readonly IPanelForgeDbContext _db;
    private readonly IAssetStorage _storage;

    public GetWorkspaceCoverQueryHandler(IPanelForgeDbContext db, IAssetStorage storage)
    {
        _db = db;
        _storage = storage;
    }

    public async Task<Result<WorkspaceCoverContent>> Handle(GetWorkspaceCoverQuery request, CancellationToken ct)
    {
        var sha = await _db.StudioWorkspaces.Where(w => w.Id == request.WorkspaceId && !w.IsDeleted)
            .Select(w => w.CoverImageSha256).FirstOrDefaultAsync(ct);
        if (sha is null)
            return NotFound();

        await using var stream = await _storage.OpenReadAsync(SetWorkspaceCoverCommandHandler.StorageKey(request.WorkspaceId, sha), ct);
        if (stream is null)
            return NotFound();

        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, ct);
        var content = buffer.ToArray();
        return Result<WorkspaceCoverContent>.Success(
            new WorkspaceCoverContent(content, AssetRules.DetectImageContentType(content) ?? "application/octet-stream"));
    }

    private static Result<WorkspaceCoverContent> NotFound()
        => Result<WorkspaceCoverContent>.Failure("Workspace chưa có ảnh bìa.", ResultErrorCodes.NotFound);
}
