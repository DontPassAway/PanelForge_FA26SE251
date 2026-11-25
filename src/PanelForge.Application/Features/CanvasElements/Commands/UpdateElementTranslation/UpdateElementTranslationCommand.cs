using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Assignments.Common;
using PanelForge.Application.Features.Chapters.Common;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.CanvasElements.Commands.UpdateElementTranslation;

/// <summary>
/// BR-34: Cập nhật bản dịch cho bóng thoại theo ngôn ngữ phát hành.
/// </summary>
public sealed record UpdateElementTranslationCommand(
    Guid PageId,
    Guid ElementId,
    string LanguageCode,
    string? Text,
    Guid UserId,
    int? ExpectedElementVersion = null
) : IRequest<Result<UpdateElementTranslationResponse>>;

public sealed record UpdateElementTranslationResponse(
    Guid ElementId,
    string LanguageCode,
    string? Text,
    int ElementVersion
);

public sealed record UpdateElementTranslationRequest(
    string? Text,
    int? ExpectedElementVersion = null
);

public sealed class UpdateElementTranslationCommandHandler
    : IRequestHandler<UpdateElementTranslationCommand, Result<UpdateElementTranslationResponse>>
{
    private readonly IPageRepository _pages;
    private readonly IPanelForgeDbContext _db;

    public UpdateElementTranslationCommandHandler(IPageRepository pages, IPanelForgeDbContext db)
    {
        _pages = pages;
        _db = db;
    }

    public async Task<Result<UpdateElementTranslationResponse>> Handle(
        UpdateElementTranslationCommand cmd, CancellationToken ct)
    {
        static Result<UpdateElementTranslationResponse> Fail(string msg, string? code = null) =>
            Result<UpdateElementTranslationResponse>.Failure(msg, code);

        var page = await _pages.GetAsync(cmd.PageId, ct);
        if (page is null) return Fail($"Page {cmd.PageId} không tồn tại.", ResultErrorCodes.NotFound);

        // BR-34: bản dịch không phụ thuộc khóa chương (ChapterApproved khóa tranh + chữ gốc, không khóa bản dịch);
        // quyền sửa do trạng thái ấn bản quyết định (chỉ Draft) — kiểm tra bên dưới.

        if (!page.Elements.TryGetValue(cmd.ElementId, out var element) || element.IsRemoved)
            return Fail($"Element {cmd.ElementId} không tồn tại trên Page {cmd.PageId}.", ResultErrorCodes.NotFound);

        // Lấy thông tin Series để kiểm tra ngôn ngữ và quyền sửa ấn bản dịch
        var seriesInfo = await (
            from c in _db.Chapters.AsNoTracking()
            where c.Id == page.ChapterId
            join s in _db.Series.AsNoTracking() on c.SeriesId equals s.Id
            select new { s.Id, s.WorkspaceId, s.OriginalLanguage, s.ReleaseLanguages }
        ).FirstOrDefaultAsync(ct);

        if (seriesInfo is null)
            return Fail("Không tìm thấy thông tin Series của trang.", ResultErrorCodes.NotFound);

        var normLang = ContentLanguages.Normalize(cmd.LanguageCode);
        var edition = normLang is not null
            ? await _db.ChapterEditions.FirstOrDefaultAsync(
                e => e.ChapterId == page.ChapterId && e.LanguageCode == normLang, ct)
            : null;

        var (canEditEditionTranslation, hasLetteringRole) = await CheckEditionTranslationPermissionAsync(
            _db,
            cmd.UserId,
            seriesInfo.Id,
            seriesInfo.WorkspaceId,
            normLang,
            seriesInfo.OriginalLanguage,
            seriesInfo.ReleaseLanguages,
            edition,
            ct);

        if (!canEditEditionTranslation)
        {
            // WorkScopeGuard: Chỉ Artist/Letterer được giao trang hoặc Producer/Owner mới có quyền ghi
            if (await WorkScopeGuard.DenyReasonAsync(_db, cmd.UserId, page.Id, element.PanelId, ct) is { } outOfScope)
                return Fail(outOfScope, ResultErrorCodes.Forbidden);

            if (edition is not null && edition.Status == EditionStatus.Draft && !hasLetteringRole)
                return Fail("Bạn không có quyền chỉnh sửa bản dịch trong Series này.", ResultErrorCodes.Forbidden);
        }

        // BR-11: Optimistic Concurrency check
        try
        {
            page.EnsureElementVersion(cmd.ElementId, cmd.ExpectedElementVersion);
        }
        catch (InvalidOperationException ex)
        {
            return Fail(ex.Message, ResultErrorCodes.Conflict);
        }

        if (normLang is null || !seriesInfo.ReleaseLanguages.Contains(normLang) ||
            normLang.Equals(seriesInfo.OriginalLanguage, StringComparison.OrdinalIgnoreCase))
        {
            return Fail(
                $"Ngôn ngữ '{cmd.LanguageCode}' không hợp lệ: phải thuộc ReleaseLanguages và khác OriginalLanguage ('{seriesInfo.OriginalLanguage}') của Series.",
                ResultErrorCodes.Validation);
        }

        // BR-19: Pencil đã nộp
        var hasPencilPending = await (
            from a in _db.Assignments.AsNoTracking()
            where a.ChapterId == page.ChapterId &&
                  (a.TargetEntityId == page.Id || a.EntityType == WorkflowEntityType.Chapter) &&
                  (a.Status == AssignmentStatus.Assigned || a.Status == AssignmentStatus.InProgress)
            join s in _db.PipelineStages.AsNoTracking() on a.PipelineStageId equals s.Id
            where s.Slug.ToLower().Contains("pencil") || s.Name.ToLower().Contains("pencil")
            select a.Id
        ).AnyAsync(ct);

        if (hasPencilPending)
        {
            return Fail("Bản vẽ Pencil của trang chưa được nộp (BR-19).", ResultErrorCodes.Conflict);
        }

        // BR-34: Chỉ được sửa bản dịch khi ấn bản ngôn ngữ đang ở trạng thái Draft
        if (edition is not null && edition.Status != EditionStatus.Draft)
        {
            return Fail($"Ấn bản '{normLang}' đang ở trạng thái {edition.Status}, chỉ có thể sửa bản dịch khi ở trạng thái Draft (BR-34).", ResultErrorCodes.Conflict);
        }

        // Cập nhật bản dịch trên aggregate và ghi nhận người dịch trên ChapterEdition (giữ BR-06: Editor != Translator)
        try
        {
            page.UpdateElementTranslation(cmd.ElementId, normLang, cmd.Text, cmd.UserId);
            await _pages.SaveAsync(page, ct);

            if (edition is not null)
            {
                edition.RecordTranslator(cmd.UserId);
                await _db.SaveChangesAsync(ct);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or ConcurrentPageUpdateException)
        {
            return Fail(ex.Message, ResultErrorCodes.Conflict);
        }

        return Result<UpdateElementTranslationResponse>.Success(
            new UpdateElementTranslationResponse(
                cmd.ElementId,
                normLang,
                cmd.Text?.Trim(),
                page.Elements[cmd.ElementId].Version));
    }

    /// <summary>
    /// BR-34: Cho phép sửa bản dịch sau khi chương đã ChapterApproved (mọi StageTask V3 đã Approved) khi cả 3 điều kiện đều thỏa:
    ///   a) ngôn ngữ đích khác OriginalLanguage và thuộc ReleaseLanguages của Series,
    ///   b) ChapterEdition cho (chapter, lang) tồn tại và đang ở trạng thái có thể sửa (Draft / trả về sửa lại),
    ///   c) người gọi là Letterer (hoặc Artist / Producer / Owner được phép làm chữ) của Series/Workspace.
    /// </summary>
    private static async Task<(bool Allowed, bool HasLetteringRole)> CheckEditionTranslationPermissionAsync(
        IPanelForgeDbContext db,
        Guid userId,
        Guid seriesId,
        Guid workspaceId,
        string? normalizedLang,
        string originalLanguage,
        IReadOnlyCollection<string> releaseLanguages,
        ChapterEdition? edition,
        CancellationToken ct)
    {
        var hasLetteringRole = await HasLetteringRoleAsync(db, userId, seriesId, workspaceId, ct);

        if (string.IsNullOrEmpty(normalizedLang)
            || !releaseLanguages.Contains(normalizedLang)
            || normalizedLang.Equals(originalLanguage, StringComparison.OrdinalIgnoreCase))
        {
            return (false, hasLetteringRole);
        }

        if (edition is null || edition.Status != EditionStatus.Draft)
        {
            return (false, hasLetteringRole);
        }

        return (hasLetteringRole, hasLetteringRole);
    }

    private static async Task<bool> HasLetteringRoleAsync(
        IPanelForgeDbContext db,
        Guid userId,
        Guid seriesId,
        Guid workspaceId,
        CancellationToken ct)
    {
        var isOwner = await db.StudioWorkspaces.AsNoTracking()
            .AnyAsync(w => w.Id == workspaceId && w.OwnerId == userId, ct);
        if (isOwner) return true;

        var seriesRole = await db.SeriesMembers.AsNoTracking()
            .Where(sm => sm.SeriesId == seriesId && sm.UserId == userId && !sm.IsDeleted)
            .Select(sm => (WorkspaceRole?)sm.Role)
            .FirstOrDefaultAsync(ct);

        var workspaceRole = await db.WorkspaceMembers.AsNoTracking()
            .Where(m => m.WorkspaceId == workspaceId && m.UserId == userId && m.Status == WorkspaceMemberStatus.Active)
            .Select(m => (WorkspaceRole?)m.Role)
            .FirstOrDefaultAsync(ct);

        var effectiveRole = seriesRole ?? workspaceRole;
        return effectiveRole is WorkspaceRole.Letterer or WorkspaceRole.Artist or WorkspaceRole.Producer;
    }
}
