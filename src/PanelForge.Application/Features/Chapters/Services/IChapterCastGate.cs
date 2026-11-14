using PanelForge.Application.Features.Chapters.Queries.GetChapterCast;

namespace PanelForge.Application.Features.Chapters.Services;

/// <summary>
/// BIB-14 (Q16, Q21, 7A): Cổng kiểm tra Series Bible theo Chương (Chapter Cast Gate).
/// Đơn nguồn kiểm tra cho GET /cast, Gửi duyệt (SubmitScriptCommand) và Bắt đầu sản xuất (Gate 1 / FreezeChapterScriptCommand).
/// </summary>
public interface IChapterCastGate
{
    /// <summary>
    /// Kiểm tra điều kiện Gửi duyệt (SubmitScriptCommand):
    /// Chặn khi còn mục is_crowd_at_bind = false mà IsStub (chờ định nghĩa) hoặc chưa có bound_revision_id.
    /// </summary>
    Task<ChapterCastSubmitGateDto> EvaluateSubmitAsync(Guid chapterId, CancellationToken ct = default);

    /// <summary>
    /// Kiểm tra điều kiện Gate 1 (Bắt đầu sản xuất / FreezeChapterScriptCommand):
    /// Toàn bộ điều kiện Gửi duyệt + mọi bản chương dùng (bound_revision_id và panel_bible_variants)
    /// đã CanonLocked (bỏ qua mục is_crowd_at_bind). Blockers kèm giải thích nguồn kế thừa.
    /// </summary>
    Task<ChapterCastStartGateDto> EvaluateStartAsync(Guid chapterId, CancellationToken ct = default);
}
