using MediatR;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Chapters.Common;

namespace PanelForge.Application.Features.Pages.Commands.RestorePageVersion;

/// <summary>
/// CF4 4.3 / US-12 (BR-01, BR-08, BR-11, BR-12): khôi phục canvas của Page về một version cũ bằng event mới (không xoá event nào).
/// Chương phải đang sản xuất (chương chỉ đọc → mở lại trước) và <paramref name="ExpectedPageVersion"/> phải khớp version hiện tại
/// để không đè thay đổi của người khác. Quyền Producer/Artist kiểm ở controller (BR-07).
/// </summary>
public sealed record RestorePageVersionCommand(Guid PageId, long Version, long ExpectedPageVersion, Guid UserId)
    : IRequest<Result<RestorePageVersionResult>>;

/// <param name="Changes">Số event đã thêm; 0 = canvas hiện tại đã giống bản đó, không ghi gì.</param>
public sealed record RestorePageVersionResult(Guid PageId, long RestoredFromVersion, long NewVersion, int Changes);

public sealed class RestorePageVersionCommandHandler : IRequestHandler<RestorePageVersionCommand, Result<RestorePageVersionResult>>
{
    private readonly IPageRepository _pages;
    private readonly IPanelForgeDbContext _db;

    public RestorePageVersionCommandHandler(IPageRepository pages, IPanelForgeDbContext db)
    {
        _pages = pages;
        _db = db;
    }

    public async Task<Result<RestorePageVersionResult>> Handle(RestorePageVersionCommand cmd, CancellationToken ct)
    {
        static Result<RestorePageVersionResult> Fail(string m, string c) => Result<RestorePageVersionResult>.Failure(m, c);

        var page = await _pages.GetAsync(cmd.PageId, ct);
        if (page is null) return Fail($"Page {cmd.PageId} không tồn tại.", ResultErrorCodes.NotFound);
        if (await ChapterEditGuard.ReadOnlyReasonAsync(_db, page.ChapterId, ct) is { } readOnly)
            return Fail(readOnly, ResultErrorCodes.ChapterReadOnly);
        if (page.Version != cmd.ExpectedPageVersion)
            return Fail($"Xung đột phiên bản: Page đã được cập nhật bởi người khác. Vui lòng tải lại " +
                        $"(expected={cmd.ExpectedPageVersion}, actual={page.Version}).", ResultErrorCodes.Conflict);
        if (cmd.Version < 1 || cmd.Version >= page.Version)
            return Fail($"version phải trong khoảng 1..{page.Version - 1}.", ResultErrorCodes.Validation);

        var target = await _pages.GetAtVersionAsync(cmd.PageId, cmd.Version, ct);
        if (target is null) return Fail($"Page {cmd.PageId} không có version {cmd.Version}.", ResultErrorCodes.NotFound);

        int changes;
        try { changes = page.RestoreTo(target, cmd.UserId); }
        catch (InvalidOperationException ex) { return Fail(ex.Message, ResultErrorCodes.Validation); }

        try { await _pages.SaveAsync(page, ct); }
        catch (ConcurrentPageUpdateException ex) { return Fail(ex.Message, ResultErrorCodes.Conflict); }

        return Result<RestorePageVersionResult>.Success(new RestorePageVersionResult(page.Id, cmd.Version, page.Version, changes));
    }
}
