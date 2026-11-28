using MediatR;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Quotas.Common;
using PanelForge.Application.Features.Quotas.Models;
using PanelForge.Domain.Entities.Workspaces;

namespace PanelForge.Application.Features.Quotas.Commands.UpdateUploadLimits;

/// <summary>
/// PUT /api/admin/upload-limits — Admin đặt giới hạn kích thước file cho toàn nền tảng. Thêm bản ghi mới (append-only),
/// áp ngay cho lượt tải lên tiếp theo của mọi Studio. File đã lưu không bị ảnh hưởng.
/// </summary>
public sealed record UpdateUploadLimitsCommand(Guid AdminId, long ArtworkMaxBytes, long ReferenceImageMaxBytes, long CoverImageMaxBytes)
    : IRequest<Result<UploadLimitsDto>>;

public sealed class UpdateUploadLimitsCommandHandler(IPanelForgeDbContext db)
    : IRequestHandler<UpdateUploadLimitsCommand, Result<UploadLimitsDto>>
{
    public async Task<Result<UploadLimitsDto>> Handle(UpdateUploadLimitsCommand command, CancellationToken ct)
    {
        if (OutOfRange(command.ArtworkMaxBytes, UploadLimitPolicy.ArtworkCeilingBytes))
            return Invalid("artwork", UploadLimitPolicy.ArtworkCeilingBytes);
        if (OutOfRange(command.ReferenceImageMaxBytes, UploadLimitPolicy.ImageCeilingBytes))
            return Invalid("ảnh tham chiếu Bible", UploadLimitPolicy.ImageCeilingBytes);
        if (OutOfRange(command.CoverImageMaxBytes, UploadLimitPolicy.ImageCeilingBytes))
            return Invalid("ảnh bìa", UploadLimitPolicy.ImageCeilingBytes);

        db.UploadLimitPolicies.Add(UploadLimitPolicy.Create(
            command.ArtworkMaxBytes, command.ReferenceImageMaxBytes, command.CoverImageMaxBytes, command.AdminId));
        await db.SaveChangesAsync(ct);

        return Result<UploadLimitsDto>.Success(await UploadLimits.GetEffectiveAsync(db, ct));
    }

    private static bool OutOfRange(long value, long ceiling) => value <= 0 || value > ceiling;

    private static Result<UploadLimitsDto> Invalid(string label, long ceiling) => Result<UploadLimitsDto>.Failure(
        $"Giới hạn {label} phải lớn hơn 0 và không quá {UploadLimits.FormatMb(ceiling)} MB.", ResultErrorCodes.Validation);
}
