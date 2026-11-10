using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.Assets.Common;
using PanelForge.Application.Features.Assets.Models;
using PanelForge.Application.Features.Assignments.Common;
using PanelForge.Domain.Entities.Assets;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Assets.Commands.UploadArtwork;

/// <summary>
/// CF2 bước 10: Artist tải artwork của một Page ở một Stage. BR-04: server tự tính SHA-256 và so với hash client gửi.
/// BR-13: nội dung trùng trong Workspace thì dùng lại Asset (không lưu thêm byte, không tốn quota), chỉ tạo AssetReference mới.
/// Cho phép người đang giữ việc InProgress ở đúng stage hoặc thành viên Artist/Producer của Workspace tải lên.
/// </summary>
public sealed record UploadArtworkCommand(
    Guid ChapterId,
    Guid PageId,
    Guid StageId,
    Guid UserId,
    byte[] Content,
    string? FileName,
    string? ClientSha256,
    string? LayerType,
    Guid? PanelId = null  // ảnh cho một Panel (khung) của Page; null = artwork cả trang
) : IRequest<Result<AssetReferenceDto>>;

public sealed class UploadArtworkCommandHandler : IRequestHandler<UploadArtworkCommand, Result<AssetReferenceDto>>
{
    private readonly IPanelForgeDbContext _db;
    private readonly IAssetStorage _storage;

    public UploadArtworkCommandHandler(IPanelForgeDbContext db, IAssetStorage storage)
    {
        _db = db;
        _storage = storage;
    }

    public async Task<Result<AssetReferenceDto>> Handle(UploadArtworkCommand cmd, CancellationToken ct)
    {
        static Result<AssetReferenceDto> Fail(string msg, string code = ResultErrorCodes.Validation) =>
            Result<AssetReferenceDto>.Failure(msg, code);

        // ── Kiểm nội dung (không cần DB) ──
        if (cmd.Content.Length == 0)
            return Fail("File rỗng.");
        if (cmd.Content.Length > AssetRules.MaxArtworkBytes)
            return Fail($"File vượt giới hạn {AssetRules.MaxArtworkBytes / (1024 * 1024)} MB.");

        var clientHash = AssetRules.NormalizeSha256(cmd.ClientSha256);
        if (clientHash is null)
            return Fail("clientSha256 phải là SHA-256 dạng 64 ký tự hex.");

        var contentType = AssetRules.DetectImageContentType(cmd.Content);
        if (contentType is null)
            return Fail("Chỉ nhận ảnh PNG, JPEG hoặc WebP.");

        // BR-04: hash client chỉ để tham khảo, server tính lại và từ chối nếu lệch
        var hash = AssetRules.ComputeSha256(cmd.Content);
        if (hash != clientHash)
            return Fail("SHA-256 client gửi không khớp nội dung server nhận được, file bị từ chối (BR-04).");

        // ── Ngữ cảnh: Chapter, Page, Stage (BR-03, BR-12) ──
        var chapter = await _db.Chapters.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cmd.ChapterId, ct);
        if (chapter is null)
            return Fail($"Chapter {cmd.ChapterId} không tồn tại.", ResultErrorCodes.NotFound);
        if (AssignmentRules.ChapterNotEditableReason(chapter) is { } locked)
            return Fail(locked, ResultErrorCodes.Conflict);

        if (!await _db.Pages.AsNoTracking().AnyAsync(p => p.Id == cmd.PageId && p.ChapterId == chapter.Id, ct))
            return Fail("Page không thuộc Chapter.", ResultErrorCodes.NotFound);

        if (cmd.PanelId is { } panelId &&
            !await _db.Panels.AsNoTracking().AnyAsync(p => p.Id == panelId && p.PageId == cmd.PageId, ct))
            return Fail("Panel không thuộc Page.", ResultErrorCodes.NotFound);

        var workspaceId = await _db.Series.AsNoTracking()
            .Where(s => s.Id == chapter.SeriesId).Select(s => s.WorkspaceId).FirstOrDefaultAsync(ct);

        var pipelineIds = await _db.PipelineDefinitions.AsNoTracking()
            .Where(d => d.SeriesId == chapter.SeriesId).Select(d => d.Id).ToListAsync(ct);

        var stageId = cmd.StageId;
        if (stageId == Guid.Empty)
        {
            stageId = await _db.PipelineStages.AsNoTracking()
                .Where(s => pipelineIds.Contains(s.PipelineDefinitionId))
                .OrderBy(s => s.StageOrder)
                .Select(s => s.Id)
                .FirstOrDefaultAsync(ct);
        }

        // Nếu series chưa có pipeline stage nào gắn kèm, lấy stage đầu tiên bất kỳ trong hệ thống
        if (stageId == Guid.Empty)
        {
            stageId = await _db.PipelineStages.AsNoTracking()
                .OrderBy(s => s.StageOrder)
                .Select(s => s.Id)
                .FirstOrDefaultAsync(ct);
        }

        // Nếu người gọi chỉ định stageId cụ thể, bắt buộc phải thuộc quy trình của Series
        if (cmd.StageId != Guid.Empty && pipelineIds.Count > 0)
        {
            var belongsToSeries = await _db.PipelineStages.AsNoTracking().AnyAsync(
                s => s.Id == cmd.StageId && pipelineIds.Contains(s.PipelineDefinitionId), ct);
            if (!belongsToSeries)
            {
                return Fail($"Công đoạn {cmd.StageId} không thuộc quy trình sản xuất của Series.", ResultErrorCodes.Validation);
            }
        }

        // Layer khai báo phải là một loại Element có thật (nếu có truyền)
        var layerType = string.IsNullOrWhiteSpace(cmd.LayerType) ? null : cmd.LayerType.Trim();
        if (layerType is not null && !await _db.ElementTypes.AnyAsync(t => t.Code == layerType, ct))
            return Fail($"Loại layer '{layerType}' không tồn tại.");

        // ── Quyền: chỉ người đang giữ việc InProgress ở đúng stage. Việc cấp Page: ảnh cả trang hoặc khung bất kỳ của trang.
        //    Việc cấp Panel: chỉ ảnh của đúng khung đó (không được ghi ảnh cả trang, vì sẽ đè phần người khác làm) ──
        var holdsTheWork = false;
        if (stageId != Guid.Empty)
        {
            holdsTheWork = await _db.Assignments.AnyAsync(a =>
                a.ChapterId == chapter.Id && a.PipelineStageId == stageId && a.AssigneeUserId == cmd.UserId &&
                a.Status == AssignmentStatus.InProgress &&
                ((a.EntityType == WorkflowEntityType.Page && a.TargetEntityId == cmd.PageId) ||
                 (a.EntityType == WorkflowEntityType.Panel && cmd.PanelId != null && a.TargetEntityId == cmd.PanelId)), ct);

            if (!holdsTheWork && await _db.Assignments.AnyAsync(a =>
                    a.ChapterId == chapter.Id && a.PipelineStageId == stageId && a.AssigneeUserId == cmd.UserId &&
                    a.Status == AssignmentStatus.InProgress && a.EntityType == WorkflowEntityType.Chapter, ct))
            {
                var carved = await StageProgressRules.CarvedOutAsync(_db, chapter.Id, stageId, ct);
                holdsTheWork = cmd.PanelId is { } pid
                    ? !carved.Panels.Contains(pid)
                    : !carved.Pages.Contains(cmd.PageId) &&
                      !await _db.Panels.AsNoTracking().AnyAsync(p => p.PageId == cmd.PageId && carved.Panels.Contains(p.Id), ct);
            }
        }

        // Hỗ trợ Artist / Producer hoặc Owner của Workspace upload trực tiếp khi không có assignment xung đột
        if (!holdsTheWork && cmd.UserId != Guid.Empty)
        {
            var hasSpecificAssignment = await _db.Assignments.AnyAsync(a =>
                a.ChapterId == chapter.Id && a.PipelineStageId == stageId &&
                ((a.EntityType == WorkflowEntityType.Page && a.TargetEntityId == cmd.PageId) ||
                 (a.EntityType == WorkflowEntityType.Panel && cmd.PanelId != null && a.TargetEntityId == cmd.PanelId)), ct);

            if (!hasSpecificAssignment)
            {
                var isOwnerOrAuthorizedRole =
                    (_db.StudioWorkspaces != null && await _db.StudioWorkspaces.AsNoTracking().AnyAsync(w =>
                        w.Id == workspaceId && (w.OwnerId == cmd.UserId || w.CreatedBy == cmd.UserId.ToString()), ct))
                    || (_db.WorkspaceMembers != null && await _db.WorkspaceMembers.AsNoTracking().AnyAsync(m =>
                        m.WorkspaceId == workspaceId && m.UserId == cmd.UserId &&
                        (m.Role == WorkspaceRole.Artist || m.Role == WorkspaceRole.Producer), ct))
                    || (_db.SeriesMembers != null && await _db.SeriesMembers.AsNoTracking().AnyAsync(sm =>
                        sm.SeriesId == chapter.SeriesId && sm.UserId == cmd.UserId, ct));

                if (isOwnerOrAuthorizedRole)
                {
                    holdsTheWork = true;
                }
            }
        }

        if (!holdsTheWork)
            return Fail(cmd.PanelId is null
                    ? "Chỉ người giữ việc cả trang (InProgress) ở stage này hoặc thành viên Artist/Producer mới được tải artwork cả trang."
                    : "Chỉ người đang làm việc (InProgress) ở stage này cho Page/khung này hoặc thành viên Artist/Producer mới được tải artwork.",
                ResultErrorCodes.Forbidden);

        // ── BR-13: dùng lại Asset nếu nội dung đã có; ngược lại giữ chỗ quota rồi lưu ──
        var asset = await _db.Assets.FirstOrDefaultAsync(a => a.WorkspaceId == workspaceId && a.Sha256 == hash, ct);
        var reused = asset is not null;
        if (asset is null)
        {
            var workspace = await _db.StudioWorkspaces!.FirstOrDefaultAsync(w => w.Id == workspaceId, ct);
            if (workspace is null)
                return Fail("Workspace không tồn tại.", ResultErrorCodes.NotFound);
            if (!workspace.TryReserveStorage(cmd.Content.Length))
                return Fail("Vượt quota dung lượng của Workspace, không thể tải thêm.", ResultErrorCodes.Conflict);

            asset = Asset.Create(workspaceId, hash, cmd.Content.Length, contentType);
            // Lưu file trước khi ghi DB: nếu Save lỗi chỉ để lại file mồ côi theo hash, vô hại và dùng lại được
            var info = await DescribeAsync(workspace.Name, chapter, cmd, stageId, contentType, layerType, ct);
            await _storage.SaveAsync(asset.StorageKey, cmd.Content, info, ct);
            _db.Assets.Add(asset);
        }

        // Phiên bản mới của (Page, Panel, Stage), chỉ thêm (BR-01)
        var versions = await _db.AssetReferences.AsNoTracking()
            .Where(r => r.PageId == cmd.PageId && r.PanelId == cmd.PanelId && r.PipelineStageId == stageId)
            .Select(r => r.VersionNumber).ToListAsync(ct);

        var reference = AssetReference.Create(
            asset.Id, workspaceId, chapter.Id, cmd.PageId, stageId,
            versions.Count == 0 ? 1 : versions.Max() + 1,
            layerType, AssetRules.SanitizeFileName(cmd.FileName), cmd.UserId, cmd.PanelId);
        reference.CreatedBy = cmd.UserId.ToString();

        _db.AssetReferences.Add(reference);
        await _db.SaveChangesAsync(ct);

        return Result<AssetReferenceDto>.Success(AssetReferenceDto.From(reference, asset, reused));
    }

    /// <summary>
    /// Nhãn dễ đọc gắn lên file trên kho (vd. Custom Metadata ở R2) để người xem bucket biết ảnh thuộc Studio/Series/trang nào.
    /// Chỉ ghi lúc lưu lần đầu – file trùng nội dung dùng lại ở chỗ khác vẫn giữ nhãn lần đầu; DB mới là nguồn sự thật.
    /// </summary>
    private async Task<AssetObjectInfo> DescribeAsync(
        string workspaceName, Chapter chapter, UploadArtworkCommand cmd, Guid stageId, string contentType, string? layerType, CancellationToken ct)
    {
        var seriesTitle = await _db.Series.AsNoTracking().Where(s => s.Id == chapter.SeriesId).Select(s => s.Title).FirstOrDefaultAsync(ct);
        var pageNumber = await _db.Pages.AsNoTracking().Where(p => p.Id == cmd.PageId).Select(p => p.PageNumber).FirstOrDefaultAsync(ct);
        int? panelNumber = cmd.PanelId is { } panelId
            ? await _db.Panels.AsNoTracking().Where(p => p.Id == panelId).Select(p => (int?)p.PanelNumber).FirstOrDefaultAsync(ct)
            : null;
        var stageName = stageId != Guid.Empty
            ? await _db.PipelineStages.AsNoTracking().Where(s => s.Id == stageId).Select(s => s.Name).FirstOrDefaultAsync(ct)
            : "Artwork";
        var uploader = await _db.Users.AsNoTracking().Where(u => u.Id == cmd.UserId).Select(u => u.Email).FirstOrDefaultAsync(ct);

        var metadata = new Dictionary<string, string>
        {
            ["workspace"] = workspaceName,
            ["series"] = seriesTitle ?? "",
            ["chapter"] = $"{chapter.ChapterNumber:0.##}{(string.IsNullOrWhiteSpace(chapter.Title) ? "" : " - " + chapter.Title)}",
            ["page"] = pageNumber.ToString(),
            ["panel"] = panelNumber?.ToString() ?? "ca trang",
            ["stage"] = stageName ?? "Artwork",
            ["layer"] = layerType ?? "",
            ["file-name"] = AssetRules.SanitizeFileName(cmd.FileName) ?? "",
            ["uploaded-by"] = uploader ?? "",
            ["uploaded-at"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
        };
        return new AssetObjectInfo(contentType, metadata);
    }
}
