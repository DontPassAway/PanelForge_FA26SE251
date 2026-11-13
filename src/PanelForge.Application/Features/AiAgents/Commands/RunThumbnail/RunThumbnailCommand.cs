using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.AI.Contracts;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.AiAgents.Common;
using PanelForge.Application.Features.AiAgents.Models;
using PanelForge.Application.Features.Assets.Common;
using PanelForge.Domain.Entities.Ai;
using PanelForge.Domain.Entities.Assets;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.AiAgents.Commands.RunThumbnail;

/// <summary>
/// Artist Thumbnail (kế hoạch AI đa vai trò, Tuần 3): sinh MỘT ảnh cho panel từ prompt ảnh đã Accept (revision ImagePrompt mới nhất của Scene).
/// Guard TRƯỚC khi tốn tiền ảnh: vai trò Artist (BR-07), panel thuộc Series (BR-03), Chapter InProduction (BR-12), người gọi đang giữ việc
/// InProgress cho panel/trang, AI bật + quota ảnh Workspace + model được phép + tối đa N job ảnh đồng thời/Series.
/// Ảnh lưu vào kho (BR-13 dùng lại theo hash) nhưng CHƯA gắn vào panel: kết quả là AiSuggestion Thumbnail chờ người Accept (BR-02).
/// </summary>
public sealed record RunThumbnailCommand(Guid SeriesId, Guid PanelId, Guid UserId, string? Instruction)
    : IRequest<Result<AiSuggestionDto>>;

public sealed class RunThumbnailCommandHandler : IRequestHandler<RunThumbnailCommand, Result<AiSuggestionDto>>
{
    private const int MaxReferences = 3, MaxReferenceBytes = 4 * 1024 * 1024, MaxPromptChars = 3500;
    private const string Feature = "AiAgents.Thumbnail";

    private readonly IPanelForgeDbContext _db;
    private readonly IWorkspaceAuthorizationService _auth;
    private readonly IAiUsageService _usage;
    private readonly IAiKeyProtector _keyProtector;
    private readonly IAiClient _aiClient;
    private readonly IAssetStorage _storage;
    private readonly AiAgentSettings _settings;

    public RunThumbnailCommandHandler(
        IPanelForgeDbContext db, IWorkspaceAuthorizationService auth, IAiUsageService usage,
        IAiKeyProtector keyProtector, IAiClient aiClient, IAssetStorage storage, AiAgentSettings settings)
    {
        _db = db;
        _auth = auth;
        _usage = usage;
        _keyProtector = keyProtector;
        _aiClient = aiClient;
        _storage = storage;
        _settings = settings;
    }

    private static Result<AiSuggestionDto> Fail(string message, string code) => AiRunHelpers.Fail<AiSuggestionDto>(message, code);

    public async Task<Result<AiSuggestionDto>> Handle(RunThumbnailCommand cmd, CancellationToken ct)
    {
        if (cmd.Instruction is { Length: > CopilotRules.MaxInstructionLength })
            return Fail($"instruction tối đa {CopilotRules.MaxInstructionLength} ký tự.", ResultErrorCodes.Validation);

        var series = await _db.Series.AsNoTracking().FirstOrDefaultAsync(s => s.Id == cmd.SeriesId, ct);
        if (series is null) return Fail($"Series {cmd.SeriesId} không tồn tại.", ResultErrorCodes.NotFound);

        var role = await _auth.GetMemberRoleAsync(cmd.UserId, series.WorkspaceId, ct);
        if (role != WorkspaceRole.Artist)
            return Fail("Chỉ Artist của Workspace mới được sinh thumbnail.", ResultErrorCodes.Forbidden);

        // BR-03: panel → page → chapter thuộc Series trên route
        var panel = await _db.Panels.AsNoTracking().FirstOrDefaultAsync(p => p.Id == cmd.PanelId, ct);
        var page = panel is null ? null : await _db.Pages.AsNoTracking().FirstOrDefaultAsync(p => p.Id == panel.PageId, ct);
        var chapter = page is null ? null
            : await _db.Chapters.AsNoTracking().FirstOrDefaultAsync(c => c.Id == page.ChapterId && c.SeriesId == series.Id, ct);
        if (panel is null || page is null || chapter is null) return Fail("Panel không thuộc Series này.", ResultErrorCodes.NotFound);
        if (!chapter.IsContentEditable)
            return Fail($"Chapter đang ở trạng thái {chapter.Status} (chỉ đọc).", ResultErrorCodes.Conflict);

        var stageId = await ThumbnailRules.HeldStageAsync(_db, cmd.UserId, chapter.Id, page.Id, panel.Id, null, ct);
        if (stageId is null)
            return Fail("Bạn cần đang giữ việc (InProgress) cho panel/trang này mới sinh được thumbnail.", ResultErrorCodes.Forbidden);

        // Prompt lấy từ revision ImagePrompt mới nhất (Artist đã Accept), client không gửi prompt tuỳ ý
        var promptRevision = panel.SceneId is null ? null : await _db.ContentRevisions.AsNoTracking()
            .Where(r => r.SceneId == panel.SceneId && r.Kind == ContentRevisionKind.ImagePrompt)
            .OrderByDescending(r => r.VersionNumber).FirstOrDefaultAsync(ct);
        var panelPrompt = promptRevision is null ? null
            : AiContentValidator.TryParse<ArtistPromptSet>(promptRevision.PayloadJson, out _)?.Panels.FirstOrDefault(p => p.PanelId == panel.Id);
        if (promptRevision is null || panelPrompt is null)
            return Fail("Panel chưa có prompt ảnh đã duyệt. Chạy nhiệm vụ 'Prompt ảnh cho panel' và Accept trước.", ResultErrorCodes.Validation);

        // ── Gate: AI bật, quota ảnh + token, model được phép, số job ảnh đồng thời của Series ──
        var config = await _db.WorkspaceAiConfigs.FirstOrDefaultAsync(c => c.WorkspaceId == series.WorkspaceId, ct);
        if (config is null || !config.IsEnabled)
            return Fail("Tính năng AI chưa được bật cho Workspace này.", ResultErrorCodes.AiDisabled);
        if (!config.HasImageQuotaFor(1))
            return Fail($"Workspace đã dùng hết quota ảnh tháng này ({config.UsedImagesCurrentMonth}/{config.MonthlyImageQuota}).",
                ResultErrorCodes.AiQuotaExceeded);

        var (provider, model) = ThumbnailRules.ResolveProvider(_settings);
        if (provider is null)
            return Fail($"AiService:ImageProvider '{_settings.ImageProvider}' không được hỗ trợ (Gemini | Cloudflare).", ResultErrorCodes.AiDisabled);
        if (!_settings.IsMock)
        {
            var availability = await _usage.CheckAvailabilityAsync(series.WorkspaceId, ct);
            if (!availability.IsAvailable)
            {
                await _usage.RecordAsync(new AiUsageEntry(series.WorkspaceId, Feature, availability.BlockedReason!.Value,
                    ErrorMessage: availability.Message, UserId: cmd.UserId), ct);
                return Fail(availability.Message ?? "AI không khả dụng.",
                    availability.BlockedReason == AiUsageStatus.QuotaExceeded ? ResultErrorCodes.AiQuotaExceeded : ResultErrorCodes.AiDisabled);
            }
            var models = JsonSerializer.Deserialize<List<string>>(config.AllowedModelsJson) ?? [];
            if (!models.Contains(model, StringComparer.OrdinalIgnoreCase))
                return Fail($"Model ảnh '{model}' chưa nằm trong danh sách model được phép của Workspace.", ResultErrorCodes.Validation);
        }

        // ponytail: đếm trên DB nên 2 request song song có thể cùng lọt ở ngưỡng; đủ để chặn lạm dụng.
        var since = DateTime.UtcNow.AddSeconds(-(_settings.ImageTimeoutSeconds + 30));
        var running = await _db.AiWorkflowRuns.AsNoTracking().CountAsync(r => r.SeriesId == series.Id
            && r.WorkflowType == AiWorkflowType.Thumbnail && (r.Status == AiRunStatus.Queued || r.Status == AiRunStatus.Running)
            && r.CreatedAt >= since, ct);
        if (running >= _settings.ImageJobsPerSeries)
            return Fail($"Series đang có {running} ảnh đang sinh (tối đa {_settings.ImageJobsPerSeries}). Chờ xong rồi thử lại.",
                ResultErrorCodes.AiQuotaExceeded);

        var prompt = string.IsNullOrWhiteSpace(cmd.Instruction) ? panelPrompt.Prompt : $"{panelPrompt.Prompt}\n\nYêu cầu thêm: {cmd.Instruction.Trim()}";
        if (prompt.Length > MaxPromptChars) prompt = prompt[..MaxPromptChars];
        // Cloudflare text-to-image không nhận ảnh tham chiếu → không đọc kho vô ích
        var references = provider == ImageProviders.Gemini ? await LoadReferencesAsync(panelPrompt.ReferenceIds, ct) : [];
        // Provenance (BR-10): Bible revision mà prompt ảnh đã dựa vào
        var bibleJson = await _db.AiSuggestions.AsNoTracking().Where(s => s.Id == promptRevision.SourceSuggestionId)
            .Select(s => s.BibleRevisionIdsJson).FirstOrDefaultAsync(ct) ?? "[]";

        var run = AiWorkflowRun.Create(series.WorkspaceId, series.Id, chapter.Id, panel.SceneId, cmd.UserId, provider, model,
            string.IsNullOrWhiteSpace(cmd.Instruction) ? "thumbnail" : $"thumbnail: {cmd.Instruction}",
            series.ReadingDirection, 0, chapter.VersionVector, null,
            AiJson.Serialize(new { panelId = panel.Id, pageId = page.Id, stageId, imagePromptRevisionId = promptRevision.Id, prompt }),
            bibleJson, AiWorkflowType.Thumbnail);
        run.Touch(); // Running: được đếm vào giới hạn job đồng thời
        _db.AiWorkflowRuns.Add(run);
        await _db.SaveChangesAsync(ct);

        string? apiKey = null;
        if (provider == ImageProviders.Gemini)
        {
            apiKey = _keyProtector.Unprotect(config.ApiKeyEncrypted ?? string.Empty);
            if (string.IsNullOrEmpty(apiKey))
            {
                run.MarkFailed(ResultErrorCodes.AiUnavailable, "Không giải mã được API key của Workspace.");
                await _db.SaveChangesAsync(ct);
                return Fail("Không giải mã được API key Gemini của Workspace. Admin cần nhập lại key.", ResultErrorCodes.AiUnavailable);
            }
        }

        var response = await _aiClient.GenerateImageAsync(new ImageGenRequest(provider, model, apiKey, prompt, references), ct);
        var bytes = response is { Succeeded: true, ImageBase64: { Length: > 0 } b64 } ? TryDecode(b64) : null;
        var contentType = bytes is null ? null : AssetRules.DetectImageContentType(bytes);
        if (response.Succeeded && (contentType is null || bytes!.Length > AssetRules.MaxArtworkBytes))
            response = response with { Succeeded = false, ErrorCode = "INVALID_OUTPUT", ErrorMessage = "Ảnh trả về không hợp lệ (không phải PNG/JPEG/WebP hoặc quá lớn)." };

        // ── Lưu ảnh vào kho (chưa gắn panel). BR-13: trùng nội dung trong Workspace thì dùng lại Asset ──
        Asset? asset = null;
        if (response.Succeeded)
        {
            var hash = AssetRules.ComputeSha256(bytes!);
            asset = await _db.Assets.FirstOrDefaultAsync(a => a.WorkspaceId == series.WorkspaceId && a.Sha256 == hash, ct);
            if (asset is null)
            {
                var workspace = await _db.StudioWorkspaces.FirstOrDefaultAsync(w => w.Id == series.WorkspaceId, ct);
                if (workspace is null || !workspace.TryReserveStorage(bytes!.Length))
                    response = response with { Succeeded = false, ErrorCode = "STORAGE_QUOTA", ErrorMessage = "Vượt quota dung lượng của Workspace, không lưu được ảnh." };
                else
                {
                    asset = Asset.Create(series.WorkspaceId, hash, bytes.Length, contentType!);
                    await _storage.SaveAsync(asset.StorageKey, bytes, new AssetObjectInfo(contentType!, new Dictionary<string, string>
                    {
                        ["series"] = series.Title, ["chapter"] = $"{chapter.ChapterNumber:0.##}", ["page"] = page.PageNumber.ToString(),
                        ["panel"] = panel.PanelNumber.ToString(), ["source"] = $"ai-thumbnail {provider}/{model}",
                        ["generated-at"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
                    }), ct);
                    _db.Assets.Add(asset);
                }
            }
            if (response.Succeeded) config.RecordImages(1);
        }

        var payload = response.Succeeded
            ? new ThumbnailSuggestion(AiProtocol.SchemaVersion, run.Id, panel.Id, page.Id, stageId.Value, asset!.Id, asset.Sha256,
                promptRevision.Id, prompt, provider, model, panelPrompt.ReferenceIds)
            : null;
        var step = await CopilotGate.RecordStepAsync(_db, _usage, _settings, run, AiAgentType.Artist,
            new CopilotProvider(provider, model, config), "thumbnail",
            new CopilotResponse(response.Succeeded, null, null, "thumbnail@1", $"image:{provider}", response.PromptTokens,
                response.CompletionTokens, 0, response.StartedAtUtc, response.CompletedAtUtc, response.ErrorCode, response.ErrorMessage),
            new { provider, model, references = references.Count, timeoutSeconds = _settings.ImageTimeoutSeconds },
            new { panelId = panel.Id, imagePromptRevisionId = promptRevision.Id, prompt, referenceEntryIds = panelPrompt.ReferenceIds, response.SafetyBlocked },
            payload is null ? null : AiJson.Serialize(payload), cmd.UserId, ct);

        if (payload is null)
        {
            run.MarkFailed(response.ErrorCode ?? ResultErrorCodes.AiUnavailable, response.ErrorMessage);
            await _db.SaveChangesAsync(ct);
            return Fail(response.ErrorMessage ?? "Không sinh được ảnh.",
                response.ErrorCode == ResultErrorCodes.AiUnavailable ? ResultErrorCodes.AiUnavailable
                : response.ErrorCode == "STORAGE_QUOTA" ? ResultErrorCodes.Conflict : ResultErrorCodes.Validation);
        }

        var json = AiJson.Serialize(payload);
        // SceneId = Empty khi panel chưa gán Scene (cột không có khoá ngoại tới Scene)
        var suggestion = AiSuggestion.Create(run.Id, step.Id, series.WorkspaceId, series.Id, chapter.Id, panel.SceneId ?? Guid.Empty,
            AiSuggestionKind.Thumbnail, json, AiContentValidator.Sha256(json),
            $"Thumbnail AI cho trang {page.PageNumber} panel {panel.PanelNumber} ({provider})", chapter.VersionVector, null,
            run.BibleRevisionIdsJson, null, false);
        _db.AiSuggestions.Add(suggestion);
        run.MarkCompleted();
        await _db.SaveChangesAsync(ct);

        return Result<AiSuggestionDto>.Success(AiRunHelpers.ToDto(suggestion, [], role));
    }

    private static byte[]? TryDecode(string b64)
    {
        try { return Convert.FromBase64String(b64); }
        catch (FormatException) { return null; }
    }

    /// <summary>Ảnh character sheet của nhân vật trong panel: data URL hoặc URL chứa id Asset trong kho. Không tải URL ngoài (tránh SSRF).</summary>
    private async Task<List<ImageReference>> LoadReferencesAsync(IReadOnlyList<Guid> entryIds, CancellationToken ct)
    {
        var entries = await _db.BibleEntries.AsNoTracking()
            .Where(e => entryIds.Contains(e.Id) && e.ReferenceImageUrl != null && e.ReferenceImageUrl != "")
            .Select(e => new { e.Name, e.ReferenceImageUrl }).Take(MaxReferences).ToListAsync(ct);
        var result = new List<ImageReference>();
        foreach (var e in entries)
        {
            var bytes = await ResolveImageAsync(e.ReferenceImageUrl!, ct);
            var mime = bytes is { Length: > 0 and <= MaxReferenceBytes } ? AssetRules.DetectImageContentType(bytes) : null;
            if (mime is not null) result.Add(new ImageReference(e.Name, mime, Convert.ToBase64String(bytes!)));
        }
        return result;
    }

    private async Task<byte[]?> ResolveImageAsync(string url, CancellationToken ct)
    {
        if (url.StartsWith("data:image", StringComparison.OrdinalIgnoreCase))
            return url.IndexOf(',') is var i and >= 0 ? TryDecode(url[(i + 1)..]) : null;
        foreach (var part in url.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!Guid.TryParse(part, out var assetId)) continue;
            var key = await _db.Assets.AsNoTracking().Where(a => a.Id == assetId).Select(a => a.StorageKey).FirstOrDefaultAsync(ct);
            if (key is null) continue;
            await using var stream = await _storage.OpenReadAsync(key, ct);
            if (stream is null) return null;
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms, ct);
            return ms.ToArray();
        }
        return null;
    }
}

/// <summary>Luật dùng chung cho sinh thumbnail và Accept thumbnail.</summary>
internal static class ThumbnailRules
{
    public static (string? Provider, string Model) ResolveProvider(AiAgentSettings s) =>
        s.IsMock ? (ImageProviders.Mock, "mock-image-v1")
        : s.ImageProvider.Equals(ImageProviders.Gemini, StringComparison.OrdinalIgnoreCase) ? (ImageProviders.Gemini, s.GeminiImageModel)
        : s.ImageProvider.Equals(ImageProviders.Cloudflare, StringComparison.OrdinalIgnoreCase) ? (ImageProviders.Cloudflare, s.CloudflareImageModel)
        : (null, "");

    /// <summary>
    /// Stage mà user đang giữ việc InProgress phủ panel này: việc cấp Panel, cấp Page, hoặc cả Chapter (cùng phạm vi với UploadArtwork).
    /// <paramref name="stageId"/> khác null = chỉ chấp nhận đúng stage đó (khi Accept).
    /// ponytail: việc cả Chapter không trừ phần đã tách sang việc Page/Panel của người khác; chặt hơn thì dùng StageProgressRules.CarvedOutAsync.
    /// </summary>
    public static async Task<Guid?> HeldStageAsync(IPanelForgeDbContext db, Guid userId, Guid chapterId, Guid pageId, Guid panelId,
        Guid? stageId, CancellationToken ct)
    {
        var held = await db.Assignments.AsNoTracking()
            .Where(a => a.ChapterId == chapterId && a.AssigneeUserId == userId && a.Status == AssignmentStatus.InProgress
                        && (stageId == null || a.PipelineStageId == stageId)
                        && ((a.EntityType == WorkflowEntityType.Panel && a.TargetEntityId == panelId)
                            || (a.EntityType == WorkflowEntityType.Page && a.TargetEntityId == pageId)
                            || a.EntityType == WorkflowEntityType.Chapter))
            .Select(a => new { a.EntityType, a.PipelineStageId }).ToListAsync(ct);
        return held.OrderBy(a => a.EntityType == WorkflowEntityType.Panel ? 0 : a.EntityType == WorkflowEntityType.Page ? 1 : 2)
            .Select(a => (Guid?)a.PipelineStageId).FirstOrDefault();
    }
}
