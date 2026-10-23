using System.Net.Mail;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Chapters.Common;
using PanelForge.Application.Features.PreviewLinks.Common;
using PanelForge.Application.Features.PreviewLinks.Models;
using PanelForge.Domain.Entities.Preview;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.PreviewLinks.Commands;

/// <summary>
/// UC-11 (BR-09, BR-17): Producer tạo link xem thử cho một email, có hạn (1–720 giờ, mặc định 72), chỉ cho Chapter đã duyệt
/// (Approved = đã khoá, BR-12). Snapshot: bản artwork cả trang ở stage cuối, mới nhất, của từng trang lúc tạo.
/// </summary>
public sealed record CreatePreviewLinkCommand(Guid ChapterId, Guid UserId, string? RecipientEmail, int? ExpiresInHours)
    : IRequest<Result<CreatedPreviewLinkDto>>;

public sealed class CreatePreviewLinkCommandHandler : IRequestHandler<CreatePreviewLinkCommand, Result<CreatedPreviewLinkDto>>
{
    private readonly IPanelForgeDbContext _db;

    public CreatePreviewLinkCommandHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<CreatedPreviewLinkDto>> Handle(CreatePreviewLinkCommand cmd, CancellationToken ct)
    {
        static Result<CreatedPreviewLinkDto> Fail(string m, string c) => Result<CreatedPreviewLinkDto>.Failure(m, c);

        if (!MailAddress.TryCreate(cmd.RecipientEmail?.Trim(), out var email))
            return Fail("Email người nhận không hợp lệ.", ResultErrorCodes.Validation);
        var hours = cmd.ExpiresInHours ?? PreviewLinkRules.DefaultHours;
        if (hours < 1 || hours > PreviewLinkRules.MaxHours)
            return Fail($"Thời hạn phải từ 1 đến {PreviewLinkRules.MaxHours} giờ.", ResultErrorCodes.Validation);

        var chapter = await _db.Chapters.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cmd.ChapterId, ct);
        if (chapter is null) return Fail($"Chapter {cmd.ChapterId} không tồn tại.", ResultErrorCodes.NotFound);
        if (chapter.Status != ChapterStatus.Approved)
            return Fail("Chỉ tạo link xem thử cho chương đã duyệt (Approved) và chưa phát hành (BR-17).", ResultErrorCodes.Conflict);

        var workspaceId = await _db.Series.AsNoTracking().Where(s => s.Id == chapter.SeriesId)
            .Select(s => s.WorkspaceId).FirstOrDefaultAsync(ct);

        // Snapshot: mọi artwork tạo nên từng trang theo FinalArtwork (ảnh cả trang, hoặc ảnh từng Panel để ghép)
        var snapshot = (await FinalArtwork.SelectAsync(_db, chapter.Id, ct)).SelectMany(a => a.AssetReferenceIds).ToList();
        if (snapshot.Count == 0)
            return Fail("Chương chưa có artwork nào để xem thử.", ResultErrorCodes.Conflict);

        var token = PreviewLinkRules.NewToken();
        var link = PreviewLink.Create(chapter.Id, workspaceId, email.Address, PreviewLinkRules.Hash(token),
            DateTime.UtcNow.AddHours(hours), cmd.UserId, snapshot, chapter.VersionVector);
        link.CreatedBy = cmd.UserId.ToString();
        _db.PreviewLinks.Add(link);
        await _db.SaveChangesAsync(ct);

        return Result<CreatedPreviewLinkDto>.Success(new CreatedPreviewLinkDto(link.Id, link.RecipientEmail, link.ExpiresAt, token));
    }
}
