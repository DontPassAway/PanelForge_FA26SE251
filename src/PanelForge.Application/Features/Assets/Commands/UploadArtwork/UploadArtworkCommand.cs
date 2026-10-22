using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
/// Chỉ người đang giữ việc InProgress ở đúng stage cho Page này mới tải lên được.
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
    private readonly Microsoft.Extensions.DependencyInjection.IServiceScopeFactory? _scopeFactory;

    public UploadArtworkCommandHandler(
        IPanelForgeDbContext db,
        IAssetStorage storage,
        Microsoft.Extensions.DependencyInjection.IServiceScopeFactory? scopeFactory = null)
    {
        _db = db;
        _storage = storage;
        _scopeFactory = scopeFactory;
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

        var pipelineIds = await _db.PipelineDefinitions.AsNoTracking()
            .Where(d => d.SeriesId == chapter.SeriesId).Select(d => d.Id).ToListAsync(ct);
        if (!await _db.PipelineStages.AsNoTracking().AnyAsync(
                s => s.Id == cmd.StageId && pipelineIds.Contains(s.PipelineDefinitionId), ct))
            return Fail("Stage không thuộc pipeline của Series.");

        // Layer khai báo phải là một loại Element có thật
        var layerType = string.IsNullOrWhiteSpace(cmd.LayerType) ? null : cmd.LayerType.Trim();
        if (layerType is not null && !await _db.ElementTypes.AnyAsync(t => t.Code == layerType, ct))
            return Fail($"Loại layer '{layerType}' không tồn tại.");

        // ── Quyền: chỉ người đang giữ việc InProgress ở đúng stage. Việc cấp Page: ảnh cả trang hoặc khung bất kỳ của trang.
        //    Việc cấp Panel: chỉ ảnh của đúng khung đó (không được ghi ảnh cả trang, vì sẽ đè phần người khác làm) ──
        var holdsTheWork = await _db.Assignments.AnyAsync(a =>
            a.ChapterId == chapter.Id && a.PipelineStageId == cmd.StageId && a.AssigneeUserId == cmd.UserId &&
            a.Status == AssignmentStatus.InProgress &&
            ((a.EntityType == WorkflowEntityType.Page && a.TargetEntityId == cmd.PageId) ||
             (a.EntityType == WorkflowEntityType.Panel && cmd.PanelId != null && a.TargetEntityId == cmd.PanelId)), ct);
        // Việc cả chương: được tải cho trang/khung còn trong phạm vi thực tế (chưa tách sang việc Page/Panel riêng).
        // Ảnh cả trang chỉ khi không phần nào của trang đã tách, để không đè lên phần người khác làm.
        if (!holdsTheWork && await _db.Assignments.AnyAsync(a =>
                a.ChapterId == chapter.Id && a.PipelineStageId == cmd.StageId && a.AssigneeUserId == cmd.UserId &&
                a.Status == AssignmentStatus.InProgress && a.EntityType == WorkflowEntityType.Chapter, ct))
        {
            var carved = await StageProgressRules.CarvedOutAsync(_db, chapter.Id, cmd.StageId, ct);
            holdsTheWork = cmd.PanelId is { } pid
                ? !carved.Panels.Contains(pid)
                : !carved.Pages.Contains(cmd.PageId) &&
                  !await _db.Panels.AsNoTracking().AnyAsync(p => p.PageId == cmd.PageId && carved.Panels.Contains(p.Id), ct);
        }
        if (!holdsTheWork)
            return Fail(cmd.PanelId is null
                    ? "Chỉ người giữ việc cả trang (InProgress) ở stage này mới được tải artwork cả trang; việc theo khung phải tải ảnh cho đúng khung."
                    : "Chỉ người đang làm việc (InProgress) ở stage này cho Page/khung này mới được tải artwork.",
                ResultErrorCodes.Forbidden);

        // ── BR-13: dùng lại Asset nếu nội dung đã có; ngược lại giữ chỗ quota rồi lưu ──
        var workspaceId = await _db.Series.AsNoTracking()
            .Where(s => s.Id == chapter.SeriesId).Select(s => s.WorkspaceId).FirstOrDefaultAsync(ct);

        var asset = await _db.Assets.FirstOrDefaultAsync(a => a.WorkspaceId == workspaceId && a.Sha256 == hash, ct);
        var reused = asset is not null;
        if (asset is null)
        {
            var workspace = await _db.StudioWorkspaces.FirstOrDefaultAsync(w => w.Id == workspaceId, ct);
            if (workspace is null)
                return Fail("Workspace không tồn tại.", ResultErrorCodes.NotFound);
            if (!workspace.TryReserveStorage(cmd.Content.Length))
                return Fail("Vượt quota dung lượng của Workspace, không thể tải thêm.", ResultErrorCodes.Conflict);

            asset = Asset.Create(workspaceId, hash, cmd.Content.Length, contentType);
            // Lưu file trước khi ghi DB: nếu Save lỗi chỉ để lại file mồ côi theo hash, vô hại và dùng lại được
            var info = await DescribeAsync(workspace.Name, chapter, cmd, contentType, layerType, ct);
            await _storage.SaveAsync(asset.StorageKey, cmd.Content, info, ct);
            _db.Assets.Add(asset);
        }

        // Phiên bản mới của (Page, Panel, Stage), chỉ thêm (BR-01)
        var versions = await _db.AssetReferences.AsNoTracking()
            .Where(r => r.PageId == cmd.PageId && r.PanelId == cmd.PanelId && r.PipelineStageId == cmd.StageId)
            .Select(r => r.VersionNumber).ToListAsync(ct);

        var reference = AssetReference.Create(
            asset.Id, workspaceId, chapter.Id, cmd.PageId, cmd.StageId,
            versions.Count == 0 ? 1 : versions.Max() + 1,
            layerType, AssetRules.SanitizeFileName(cmd.FileName), cmd.UserId, cmd.PanelId);
        reference.CreatedBy = cmd.UserId.ToString();

        _db.AssetReferences.Add(reference);
        await _db.SaveChangesAsync(ct);

        // Bước A5: Chạy kiểm tra nhất quán hình ảnh bất đồng bộ (Background Job) để không chặn Artist upload
        if (cmd.PanelId.HasValue && _scopeFactory != null)
        {
            var pId = cmd.PanelId.Value;
            var revRef = reference.Id.ToString();
            _ = Task.Run(async () =>
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var consistencyService = scope.ServiceProvider.GetService<IImageConsistencyService>();
                    if (consistencyService != null)
                    {
                        await consistencyService.CheckPanelConsistencyAsync(pId, revRef, cancellationToken: CancellationToken.None);
                    }
                }
                catch
                {
                    // Lỗi background không làm gián đoạn request của người dùng
                }
            });
        }

        return Result<AssetReferenceDto>.Success(AssetReferenceDto.From(reference, asset, reused));
    }

    /// <summary>
    /// Nhãn dễ đọc gắn lên file trên kho (vd. Custom Metadata ở R2) để người xem bucket biết ảnh thuộc Studio/Series/trang nào.
    /// Chỉ ghi lúc lưu lần đầu – file trùng nội dung dùng lại ở chỗ khác vẫn giữ nhãn lần đầu; DB mới là nguồn sự thật.
    /// </summary>
    private async Task<AssetObjectInfo> DescribeAsync(
        string workspaceName, Chapter chapter, UploadArtworkCommand cmd, string contentType, string? layerType, CancellationToken ct)
    {
        var seriesTitle = await _db.Series.AsNoTracking().Where(s => s.Id == chapter.SeriesId).Select(s => s.Title).FirstOrDefaultAsync(ct);
        var pageNumber = await _db.Pages.AsNoTracking().Where(p => p.Id == cmd.PageId).Select(p => p.PageNumber).FirstOrDefaultAsync(ct);
        int? panelNumber = cmd.PanelId is { } panelId
            ? await _db.Panels.AsNoTracking().Where(p => p.Id == panelId).Select(p => (int?)p.PanelNumber).FirstOrDefaultAsync(ct)
            : null;
        var stageName = await _db.PipelineStages.AsNoTracking().Where(s => s.Id == cmd.StageId).Select(s => s.Name).FirstOrDefaultAsync(ct);
        var uploader = await _db.Users.AsNoTracking().Where(u => u.Id == cmd.UserId).Select(u => u.Email).FirstOrDefaultAsync(ct);

        var metadata = new Dictionary<string, string>
        {
            ["workspace"] = workspaceName,
            ["series"] = seriesTitle ?? "",
            ["chapter"] = $"{chapter.ChapterNumber:0.##}{(string.IsNullOrWhiteSpace(chapter.Title) ? "" : " - " + chapter.Title)}",
            ["page"] = pageNumber.ToString(),
            ["panel"] = panelNumber?.ToString() ?? "ca trang",
            ["stage"] = stageName ?? "",
            ["layer"] = layerType ?? "",
            ["file-name"] = AssetRules.SanitizeFileName(cmd.FileName) ?? "",
            ["uploaded-by"] = uploader ?? "",
            ["uploaded-at"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
        };
        return new AssetObjectInfo(contentType, metadata);
    }
}
