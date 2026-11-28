using MediatR;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Quotas.Common;
using PanelForge.Application.Features.Quotas.Models;

namespace PanelForge.Application.Features.Quotas.Queries.GetUploadLimits;

/// <summary>GET /api/assets/upload-limits — giới hạn kích thước file đang áp, mọi người đã đăng nhập (FE kiểm trước khi gửi).</summary>
public sealed record GetUploadLimitsQuery : IRequest<Result<UploadLimitsDto>>;

public sealed class GetUploadLimitsQueryHandler(IPanelForgeDbContext db) : IRequestHandler<GetUploadLimitsQuery, Result<UploadLimitsDto>>
{
    public async Task<Result<UploadLimitsDto>> Handle(GetUploadLimitsQuery request, CancellationToken ct)
        => Result<UploadLimitsDto>.Success(await UploadLimits.GetEffectiveAsync(db, ct));
}
