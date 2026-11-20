using MediatR;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;

namespace PanelForge.Application.Features.Users.Library;

/// <summary>GET thư viện cá nhân của chính người gọi (không bao giờ trả dữ liệu của người khác).</summary>
public sealed record GetMyLibraryQuery(Guid UserId) : IRequest<Result<ReaderLibraryDto>>;

public sealed class GetMyLibraryQueryHandler : IRequestHandler<GetMyLibraryQuery, Result<ReaderLibraryDto>>
{
    private readonly IPanelForgeDbContext _db;

    public GetMyLibraryQueryHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<ReaderLibraryDto>> Handle(GetMyLibraryQuery q, CancellationToken ct) =>
        Result<ReaderLibraryDto>.Success(await ReaderLibrary.LoadAsync(_db, q.UserId, ct));
}
