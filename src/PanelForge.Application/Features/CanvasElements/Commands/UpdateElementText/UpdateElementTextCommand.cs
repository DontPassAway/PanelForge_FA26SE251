using MediatR;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Chapters.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.CanvasElements.Common;

namespace PanelForge.Application.Features.CanvasElements.Commands.UpdateElementText;

/// <summary>
/// Letterer sửa chữ, làm mới chữ lỗi thời sau khi Writer sửa thoại, hoặc gắn lại element sang ScriptLine khác (BR-05).
/// ScriptLineId = null: giữ dòng đang gắn (nếu có). Content bỏ trống và element có dòng: lấy nguyên văn dòng hiện tại.
/// </summary>
public sealed record UpdateElementTextCommand(
    Guid PageId, Guid ElementId, string? Content, Guid? ScriptLineId, Guid UserId, int? ExpectedElementVersion = null)
    : IRequest<Result<UpdateElementTextResponse>>;

/// <summary>ElementVersion = phiên bản thật sau khi lưu; client dùng số này cho lệnh kế tiếp (BR-11), không tự +1.</summary>
public sealed record UpdateElementTextResponse(
    Guid ElementId, string? Content, Guid? ScriptLineId, Guid? SpeakerId, int ElementVersion = 0);

public sealed class UpdateElementTextCommandHandler
    : IRequestHandler<UpdateElementTextCommand, Result<UpdateElementTextResponse>>
{
    private readonly IPageRepository _pages;
    private readonly IPanelForgeDbContext _db;

    public UpdateElementTextCommandHandler(IPageRepository pages, IPanelForgeDbContext db)
    {
        _pages = pages;
        _db = db;
    }

    public async Task<Result<UpdateElementTextResponse>> Handle(UpdateElementTextCommand cmd, CancellationToken ct)
    {
        static Result<UpdateElementTextResponse> Fail(string msg) => Result<UpdateElementTextResponse>.Failure(msg);

        var page = await _pages.GetAsync(cmd.PageId, ct);
        if (page is null) return Fail($"Page {cmd.PageId} không tồn tại.");
        if (await ChapterEditGuard.ReadOnlyReasonAsync(_db, page.ChapterId, ct) is { } readOnly)
            return Result<UpdateElementTextResponse>.Failure(readOnly, ResultErrorCodes.ChapterReadOnly);
        if (!page.Elements.TryGetValue(cmd.ElementId, out var element) || element.IsRemoved)
            return Fail($"Element {cmd.ElementId} không tồn tại trên Page {cmd.PageId}.");
        try { page.EnsureElementVersion(cmd.ElementId, cmd.ExpectedElementVersion); }
        catch (InvalidOperationException ex) { return Fail(ex.Message); }

        var lineId = cmd.ScriptLineId ?? element.ScriptLineId;
        var content = cmd.Content;
        Guid? speakerId = null;
        string? sourceText = null;

        if (lineId is { } id)
        {
            var (line, error) = await LetteringPageAnalysis.ResolveLineAsync(_db, id, page, element.ElementType, ct);
            if (line is null) return Fail(error!);
            speakerId = line.Speaker;
            sourceText = line.Text;
            content = string.IsNullOrWhiteSpace(content) ? line.Text : content;
        }
        else if (string.IsNullOrWhiteSpace(content))
        {
            return Fail("Content không được rỗng khi element không gắn ScriptLine.");
        }

        try
        {
            page.UpdateElementText(cmd.ElementId, content, cmd.UserId, lineId, speakerId, sourceText);
            await _pages.SaveAsync(page, ct);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ConcurrentPageUpdateException)
        {
            return Fail(ex.Message);
        }

        return Result<UpdateElementTextResponse>.Success(
            new UpdateElementTextResponse(cmd.ElementId, content?.Trim(), lineId, speakerId,
                page.Elements[cmd.ElementId].Version));
    }
}
