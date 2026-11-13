using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.Chapters.Common;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.ScriptComments;

public sealed record ScriptCommentDto(
    Guid Id,
    Guid ChapterId,
    Guid? SceneId,
    Guid? ScriptLineId,
    string? QuotedText,
    string Body,
    Guid AuthorUserId,
    string? AuthorName,
    DateTime CreatedAt,
    DateTime? ResolvedAt,
    Guid? ResolvedById);

/// <summary>D12: mọi thành viên Workspace đọc góp ý kịch bản của chương.</summary>
public sealed record GetScriptCommentsQuery(Guid ChapterId, Guid UserId) : IRequest<Result<IReadOnlyList<ScriptCommentDto>>>;

/// <summary>D12: Editor (chính) hoặc Producer góp ý theo dòng; không đổi trạng thái kịch bản.</summary>
public sealed record AddScriptCommentCommand(
    Guid ChapterId, Guid UserId, Guid? SceneId, Guid? ScriptLineId, string? QuotedText, string Body)
    : IRequest<Result<ScriptCommentDto>>;

/// <summary>D12: Writer (hoặc người viết góp ý) đánh dấu góp ý đã xử lý.</summary>
public sealed record ResolveScriptCommentCommand(Guid ChapterId, Guid CommentId, Guid UserId) : IRequest<Result<ScriptCommentDto>>;

public sealed class ScriptCommentHandlers :
    IRequestHandler<GetScriptCommentsQuery, Result<IReadOnlyList<ScriptCommentDto>>>,
    IRequestHandler<AddScriptCommentCommand, Result<ScriptCommentDto>>,
    IRequestHandler<ResolveScriptCommentCommand, Result<ScriptCommentDto>>
{
    private readonly IPanelForgeDbContext _db;
    private readonly IWorkspaceAuthorizationService _auth;

    public ScriptCommentHandlers(IPanelForgeDbContext db, IWorkspaceAuthorizationService auth)
    {
        _db = db;
        _auth = auth;
    }

    public async Task<Result<IReadOnlyList<ScriptCommentDto>>> Handle(GetScriptCommentsQuery q, CancellationToken ct)
    {
        var role = await RoleAsync(q.ChapterId, q.UserId, ct);
        if (!role.IsSuccess) return Result<IReadOnlyList<ScriptCommentDto>>.Failure(role.ErrorMessage!, role.ErrorCode);

        var comments = await _db.ScriptComments.AsNoTracking()
            .Where(c => c.ChapterId == q.ChapterId)
            .OrderBy(c => c.CreatedAt)
            .ToListAsync(ct);
        var names = await NamesAsync(comments.Select(c => c.AuthorUserId), ct);
        return Result<IReadOnlyList<ScriptCommentDto>>.Success(comments.Select(c => ToDto(c, names)).ToList());
    }

    public async Task<Result<ScriptCommentDto>> Handle(AddScriptCommentCommand cmd, CancellationToken ct)
    {
        var role = await RoleAsync(cmd.ChapterId, cmd.UserId, ct);
        if (!role.IsSuccess) return Result<ScriptCommentDto>.Failure(role.ErrorMessage!, role.ErrorCode);
        if (role.Value is not (WorkspaceRole.Editor or WorkspaceRole.Producer))
            return Result<ScriptCommentDto>.Failure("Chỉ Editor hoặc Producer góp ý kịch bản.", ResultErrorCodes.Forbidden);

        if (string.IsNullOrWhiteSpace(cmd.Body))
            return Result<ScriptCommentDto>.Failure("Góp ý không được để trống.", ResultErrorCodes.Validation);
        if (cmd.Body.Trim().Length > ScriptComment.MaxBodyLength)
            return Result<ScriptCommentDto>.Failure($"Góp ý tối đa {ScriptComment.MaxBodyLength} ký tự.", ResultErrorCodes.Validation);

        if (cmd.ScriptLineId is { } lineId &&
            !await _db.ScriptLines.AnyAsync(l => l.Id == lineId &&
                ((l.PanelId != null && l.Panel!.Page.ChapterId == cmd.ChapterId) || l.Scene.ChapterId == cmd.ChapterId), ct))
            return Result<ScriptCommentDto>.Failure("Dòng kịch bản không thuộc chương này.", ResultErrorCodes.Validation);

        var comment = ScriptComment.Create(cmd.ChapterId, cmd.SceneId, cmd.ScriptLineId, cmd.QuotedText, cmd.Body, cmd.UserId);
        _db.ScriptComments.Add(comment);

        var chapter = await _db.Chapters.FirstAsync(c => c.Id == cmd.ChapterId, ct);
        var preview = comment.Body.Length > 120 ? comment.Body[..120] + "…" : comment.Body;
        await ScriptNotifications.NotifyWritersAsync(_db, chapter, cmd.UserId,
            $"Góp ý mới trên kịch bản Chương {chapter.ChapterNumber}", preview, ct);

        await _db.SaveChangesAsync(ct);

        var names = await NamesAsync([cmd.UserId], ct);
        return Result<ScriptCommentDto>.Success(ToDto(comment, names));
    }

    public async Task<Result<ScriptCommentDto>> Handle(ResolveScriptCommentCommand cmd, CancellationToken ct)
    {
        var role = await RoleAsync(cmd.ChapterId, cmd.UserId, ct);
        if (!role.IsSuccess) return Result<ScriptCommentDto>.Failure(role.ErrorMessage!, role.ErrorCode);

        var comment = await _db.ScriptComments.FirstOrDefaultAsync(c => c.Id == cmd.CommentId && c.ChapterId == cmd.ChapterId, ct);
        if (comment is null)
            return Result<ScriptCommentDto>.Failure("Góp ý không tồn tại.", ResultErrorCodes.NotFound);
        if (role.Value != WorkspaceRole.Writer && comment.AuthorUserId != cmd.UserId)
            return Result<ScriptCommentDto>.Failure("Chỉ Writer hoặc người viết góp ý được đánh dấu đã xử lý.", ResultErrorCodes.Forbidden);

        comment.Resolve(cmd.UserId);
        await _db.SaveChangesAsync(ct);

        var names = await NamesAsync([comment.AuthorUserId], ct);
        return Result<ScriptCommentDto>.Success(ToDto(comment, names));
    }

    private async Task<Result<WorkspaceRole>> RoleAsync(Guid chapterId, Guid userId, CancellationToken ct)
    {
        var workspaceId = await _db.Chapters.AsNoTracking()
            .Where(c => c.Id == chapterId)
            .Join(_db.Series, c => c.SeriesId, s => s.Id, (c, s) => (Guid?)s.WorkspaceId)
            .FirstOrDefaultAsync(ct);
        if (workspaceId is null)
            return Result<WorkspaceRole>.Failure($"Chương {chapterId} không tồn tại.", ResultErrorCodes.NotFound);

        var role = await _auth.GetMemberRoleAsync(userId, workspaceId.Value, ct);
        return role is null
            ? Result<WorkspaceRole>.Failure("Bạn không phải thành viên của Workspace này.", ResultErrorCodes.Forbidden)
            : Result<WorkspaceRole>.Success(role.Value);
    }

    private async Task<Dictionary<Guid, string>> NamesAsync(IEnumerable<Guid> userIds, CancellationToken ct)
    {
        var ids = userIds.Distinct().ToList();
        return await _db.Users.AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
    }

    private static ScriptCommentDto ToDto(ScriptComment c, IReadOnlyDictionary<Guid, string> names) => new(
        c.Id, c.ChapterId, c.SceneId, c.ScriptLineId, c.QuotedText, c.Body, c.AuthorUserId,
        names.GetValueOrDefault(c.AuthorUserId), c.CreatedAt, c.ResolvedAt, c.ResolvedById);
}
