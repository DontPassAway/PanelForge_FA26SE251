using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.Assets.Common;
using PanelForge.Application.Features.Series.Commands.SetSeriesCover;

namespace PanelForge.Application.Features.Chapters.Commands.SetChapterCover;

/// <summary>
/// Producer đặt ảnh bìa chương (PNG/JPEG/WebP, ≤ 5 MB) hoặc xoá bìa (<see cref="Content"/> = null). Cùng luật với bìa Series:
/// file lưu theo SHA-256 nội dung nên không ghi đè bìa cũ, Chapter chỉ giữ hash bìa hiện tại. Bìa là metadata hiển thị,
/// không phải nội dung sản xuất, nên đổi được cả khi chương đã khoá/phát hành.
/// </summary>
public sealed record SetChapterCoverCommand(Guid ChapterId, Guid UserId, byte[]? Content) : IRequest<Result<string?>>;

public sealed class SetChapterCoverCommandHandler : IRequestHandler<SetChapterCoverCommand, Result<string?>>
{
    public const long MaxBytes = SetSeriesCoverCommandHandler.MaxBytes;

    private readonly IPanelForgeDbContext _db;
    private readonly IAssetStorage _storage;

    public SetChapterCoverCommandHandler(IPanelForgeDbContext db, IAssetStorage storage)
    {
        _db = db;
        _storage = storage;
    }

    public static string StorageKey(Guid chapterId, string sha256) => $"chapter-covers/{chapterId:N}/{sha256}";

    /// <summary>URL ảnh bìa cho DTO; <c>v</c> đổi theo nội dung để trình duyệt không giữ bìa cũ.</summary>
    public static string? CoverUrl(Guid chapterId, string? sha256)
        => sha256 is null ? null : $"/api/chapters/{chapterId}/cover?v={sha256[..12]}";

    public async Task<Result<string?>> Handle(SetChapterCoverCommand cmd, CancellationToken ct)
    {
        var chapter = await _db.Chapters.FirstOrDefaultAsync(c => c.Id == cmd.ChapterId, ct);
        if (chapter is null)
            return Result<string?>.Failure($"Chapter {cmd.ChapterId} không tồn tại.", ResultErrorCodes.NotFound);

        string? sha = null;
        if (cmd.Content is not null)
        {
            if (cmd.Content.Length == 0 || cmd.Content.Length > MaxBytes)
                return Result<string?>.Failure("Ảnh bìa phải từ 1 byte đến 5 MB.", ResultErrorCodes.Validation);

            var contentType = AssetRules.DetectImageContentType(cmd.Content);
            if (contentType is null)
                return Result<string?>.Failure("Chỉ nhận ảnh PNG, JPEG hoặc WebP.", ResultErrorCodes.Validation);

            sha = AssetRules.ComputeSha256(cmd.Content);
            await _storage.SaveAsync(StorageKey(chapter.Id, sha), cmd.Content,
                new AssetObjectInfo(contentType, new Dictionary<string, string> { ["kind"] = "chapter-cover" }), ct);
        }

        chapter.SetCover(sha);
        chapter.UpdatedBy = cmd.UserId.ToString();
        await _db.SaveChangesAsync(ct);
        return Result<string?>.Success(CoverUrl(chapter.Id, sha));
    }
}
