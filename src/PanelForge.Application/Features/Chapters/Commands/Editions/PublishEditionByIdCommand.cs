using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Chapters.Commands.PublishChapter;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Chapters.Commands.Editions;

/// <summary>
/// POST /api/editions/{editionId}/publish|schedule (Quyết định D1: xuất bản thuộc về ấn bản). Ấn bản ngôn ngữ gốc phát hành
/// cùng chương (<see cref="PublishChapterCommand"/>, có hẹn giờ); ấn bản dịch đi qua <see cref="PublishEditionCommand"/>
/// (BR-34, chưa hỗ trợ hẹn giờ). Trước đây route này truyền nhầm editionId làm chapterId nên luôn 404.
/// </summary>
public sealed record PublishEditionByIdCommand(Guid EditionId, Guid UserId, DateTime? PublishAt) : IRequest<Result<object>>;

public sealed class PublishEditionByIdCommandHandler : IRequestHandler<PublishEditionByIdCommand, Result<object>>
{
    private readonly IPanelForgeDbContext _db;
    private readonly ISender _sender;

    public PublishEditionByIdCommandHandler(IPanelForgeDbContext db, ISender sender)
    {
        _db = db;
        _sender = sender;
    }

    public async Task<Result<object>> Handle(PublishEditionByIdCommand cmd, CancellationToken ct)
    {
        var edition = await _db.ChapterEditions.AsNoTracking()
            .Where(e => e.Id == cmd.EditionId)
            .Select(e => new { e.ChapterId, e.LanguageCode })
            .FirstOrDefaultAsync(ct);
        if (edition is null)
            return Result<object>.Failure($"Ấn bản {cmd.EditionId} không tồn tại.", ResultErrorCodes.NotFound);

        var originalLanguage = await _db.Chapters.AsNoTracking()
            .Where(c => c.Id == edition.ChapterId)
            .Join(_db.Series, c => c.SeriesId, s => s.Id, (c, s) => s.OriginalLanguage)
            .FirstOrDefaultAsync(ct) ?? ContentLanguages.Original;

        if (edition.LanguageCode == ContentLanguages.Normalize(originalLanguage))
            return Wrap(await _sender.Send(new PublishChapterCommand(edition.ChapterId, cmd.UserId, cmd.PublishAt), ct));

        if (cmd.PublishAt is not null)
            return Result<object>.Failure(
                "Hẹn giờ chỉ áp dụng cho ấn bản ngôn ngữ gốc (phát hành cùng chương). Ấn bản dịch phát hành ngay sau khi được nghiệm thu.",
                ResultErrorCodes.Validation);

        return Wrap(await _sender.Send(new PublishEditionCommand(edition.ChapterId, edition.LanguageCode, cmd.UserId), ct));
    }

    private static Result<object> Wrap<T>(Result<T> r) =>
        r.IsSuccess ? Result<object>.Success(r.Value!) : Result<object>.Failure(r.ErrorMessage!, r.ErrorCode);
}
