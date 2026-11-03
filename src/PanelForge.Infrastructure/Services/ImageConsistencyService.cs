using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.AI.Contracts;
using PanelForge.Application.Features.AiAgents.Models;
using PanelForge.Application.Features.Assets.Common;
using PanelForge.Application.Features.Consistency.Common;
using PanelForge.Application.Features.Consistency.Models;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;

namespace PanelForge.Infrastructure.Services;

/// <summary>
/// Kiểm tra nhất quán hình ảnh (Bước A5): tải artwork + character sheet đã khoá, nhờ PanelForge.AI (VisionAgent) chấm điểm,
/// ghi ConsistencyFinding mức Warning khi dưới ngưỡng. Đây là đường DUY NHẤT để kiểm ảnh (panel, trang, cả chương đều đi qua đây).
/// </summary>
public sealed class ImageConsistencyService : IImageConsistencyService
{
    private readonly IPanelForgeDbContext _db;
    private readonly IAssetStorage _storage;
    private readonly IAiKeyProtector _keyProtector;
    private readonly IAiUsageService _usageService;
    private readonly IAiClient _aiClient;
    private readonly AiAgentSettings _aiSettings;
    private readonly ILogger<ImageConsistencyService> _logger;

    public ImageConsistencyService(
        IPanelForgeDbContext db,
        IAssetStorage storage,
        IAiKeyProtector keyProtector,
        IAiUsageService usageService,
        IAiClient aiClient,
        AiAgentSettings aiSettings,
        ILogger<ImageConsistencyService> logger)
    {
        _db = db;
        _storage = storage;
        _keyProtector = keyProtector;
        _usageService = usageService;
        _aiClient = aiClient;
        _aiSettings = aiSettings;
        _logger = logger;
    }

    public async Task<ImageConsistencyResultDto> CheckPanelConsistencyAsync(
        Guid panelId,
        string? revisionRef = null,
        decimal? threshold = null,
        CancellationToken cancellationToken = default)
    {
        var targetThreshold = threshold ?? ConsistencyChecker.DefaultVisualSimilarityThreshold;

        // 1. Tải thông tin Panel, Page, Chapter, Series, Workspace
        var panel = await _db.Panels.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == panelId, cancellationToken);
        if (panel is null)
            throw new KeyNotFoundException($"Panel {panelId} không tồn tại.");

        var page = await _db.Pages.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == panel.PageId, cancellationToken);
        if (page is null)
            throw new KeyNotFoundException($"Trang chứa Panel {panelId} không tồn tại.");

        var chapter = await _db.Chapters.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == page.ChapterId, cancellationToken);
        if (chapter is null)
            throw new KeyNotFoundException($"Chapter chứa Panel {panelId} không tồn tại.");

        var series = await _db.Series.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == chapter.SeriesId, cancellationToken);
        if (series is null)
            throw new KeyNotFoundException($"Series của Chapter {chapter.Id} không tồn tại.");

        var workspaceId = series.WorkspaceId;

        // 2. Tìm Artwork của Panel (theo revisionRef nếu có, hoặc bản mới nhất)
        var artworkQuery = _db.AssetReferences.AsNoTracking()
            .Where(a => a.PanelId == panel.Id || (a.PageId == page.Id && a.PanelId == null));

        if (!string.IsNullOrWhiteSpace(revisionRef) && Guid.TryParse(revisionRef, out var refId))
        {
            artworkQuery = artworkQuery.Where(a => a.Id == refId);
        }

        var artworkRef = await artworkQuery
            .OrderByDescending(a => a.VersionNumber)
            .FirstOrDefaultAsync(cancellationToken);

        byte[]? artworkBytes = null;
        string artworkMime = "image/png";

        if (artworkRef is not null)
        {
            var asset = await _db.Assets.AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == artworkRef.AssetId, cancellationToken);
            if (asset is not null)
            {
                artworkMime = asset.ContentType;
                artworkBytes = await ReadAssetBytesAsync(asset.StorageKey, cancellationToken);
            }
        }

        // Không có ảnh thật → không gọi AI (không so với ảnh giả, không tốn token, không tạo finding sai)
        if (artworkBytes is null || artworkBytes.Length == 0)
            return Skipped(panel.Id, targetThreshold, "Panel chưa có ảnh artwork để kiểm.", revisionRef);

        // 3. Tìm nhân vật xuất hiện trong Panel qua ScriptLines
        var speakerIds = await _db.ScriptLines.AsNoTracking()
            .Where(l => l.PanelId == panel.Id && l.SpeakerCharacterId.HasValue)
            .Select(l => l.SpeakerCharacterId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);

        var bibleIds = await _db.SeriesBibles.AsNoTracking()
            .Where(b => b.SeriesId == series.Id)
            .Select(b => b.Id)
            .ToListAsync(cancellationToken);

        var characterEntries = await _db.BibleEntries.AsNoTracking()
            .Include(e => e.Revisions)
            .Where(e => bibleIds.Contains(e.SeriesBibleId) && e.Category == BibleEntryCategory.Character)
            .ToListAsync(cancellationToken);

        // Chỉ nhân vật thật sự có mặt trong Panel; ưu tiên nhân vật đã có character sheet.
        // ponytail: kiểm 1 nhân vật/panel; nhiều nhân vật thì lặp theo từng nhân vật khi cần.
        var targetCharacter = characterEntries.Where(c => speakerIds.Contains(c.Id))
            .OrderByDescending(c => !string.IsNullOrWhiteSpace(c.ReferenceImageUrl))
            .FirstOrDefault();

        if (targetCharacter is null)
        {
            // Không có nhân vật nào trong Series Bible để đối chiếu
            return new ImageConsistencyResultDto(
                PanelId: panel.Id,
                CharacterId: null,
                CharacterName: null,
                CharacterSheetVersion: null,
                Score: 1.0m,
                Threshold: targetThreshold,
                Passed: true,
                SummaryReason: "Chương / Panel chưa gắn nhân vật trong Series Bible.",
                Deviations: Array.Empty<ImageConsistencyDeviationDto>(),
                RevisionRef: revisionRef,
                Skipped: true
            );
        }

        // Tìm Character Sheet đã khoá hiệu lực tại chương này (BR-15)
        var lockedRev = targetCharacter.Revisions
            .Where(r => r.LockedAt != null && r.EffectiveFromChapterNumber <= chapter.ChapterNumber)
            .OrderByDescending(r => r.VersionNumber)
            .FirstOrDefault();

        var characterVersion = lockedRev?.VersionNumber ?? 1;

        // Tải ảnh Character Sheet
        byte[]? sheetBytes = null;

        if (!string.IsNullOrWhiteSpace(targetCharacter.ReferenceImageUrl))
        {
            sheetBytes = await ResolveImageBytesAsync(targetCharacter.ReferenceImageUrl, cancellationToken);
        }

        if (sheetBytes is null || sheetBytes.Length == 0)
            return Skipped(panel.Id, targetThreshold,
                $"Nhân vật '{targetCharacter.Name}' chưa có ảnh character sheet (ReferenceImageUrl) để đối chiếu.", revisionRef);

        var input = new ImageConsistencyInput(
            PanelId: panel.Id,
            RevisionRef: artworkRef?.Id.ToString() ?? revisionRef,
            AssetReferenceId: artworkRef?.Id,
            WorkspaceId: workspaceId,
            ChapterId: chapter.Id,
            CharacterName: targetCharacter.Name,
            CharacterId: targetCharacter.Id,
            CharacterVersion: characterVersion,
            CharacterSheetBytes: sheetBytes,
            CharacterSheetMimeType: SniffMime(sheetBytes),
            ArtworkBytes: artworkBytes,
            ArtworkMimeType: artworkMime,
            Threshold: targetThreshold
        );

        return await CompareImagesAsync(input, cancellationToken);
    }

    public async Task<ImageConsistencyResultDto> CompareImagesAsync(
        ImageConsistencyInput input,
        CancellationToken cancellationToken = default)
    {
        const string feature = "Consistency.ImageConsistencyCheck";
        var threshold = input.Threshold ?? ConsistencyChecker.DefaultVisualSimilarityThreshold;

        // Cùng cổng với mọi tính năng AI: Workspace bật AI + còn quota (UC-02, E-29). Gọi qua PanelForge.AI như mọi agent khác:
        // một nơi giữ key/gọi provider, cùng timeout/retry/giãn cách. Key chỉ lấy từ Workspace (không còn key dự phòng cấp hệ thống).
        string provider, model;
        string? apiKey = null;
        if (_aiSettings.IsMock) (provider, model) = (AiProtocol.ProviderMock, "mock-fixture-v1");
        else
        {
            var availability = await _usageService.CheckAvailabilityAsync(input.WorkspaceId, cancellationToken);
            if (!availability.IsAvailable)
                throw new InvalidOperationException(availability.Message ?? "AI không khả dụng cho Workspace này.");
            (provider, model) = (AiProtocol.ProviderGemini, _aiSettings.GeminiModel);
            var config = await _db.WorkspaceAiConfigs.AsNoTracking()
                .FirstOrDefaultAsync(c => c.WorkspaceId == input.WorkspaceId, cancellationToken);
            apiKey = string.IsNullOrWhiteSpace(config?.ApiKeyEncrypted) ? null : _keyProtector.Unprotect(config.ApiKeyEncrypted);
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new InvalidOperationException("Workspace chưa có API key Gemini hợp lệ (Admin cần nhập/nhập lại key).");
        }

        var response = await _aiClient.CompareImagesAsync(new VisionCompareRequest(provider, model, apiKey, input.CharacterName,
            new ImageReference("character-sheet", input.CharacterSheetMimeType, Convert.ToBase64String(input.CharacterSheetBytes)),
            new ImageReference("artwork", input.ArtworkMimeType, Convert.ToBase64String(input.ArtworkBytes))), cancellationToken);

        var durationMs = (int)(response.CompletedAtUtc - response.StartedAtUtc).TotalMilliseconds;
        if (!_aiSettings.IsMock)
            await _usageService.RecordAsync(new AiUsageEntry(input.WorkspaceId, feature,
                response.Succeeded ? AiUsageStatus.Succeeded
                    : response.ErrorCode is "AI_UNAVAILABLE" or "PROVIDER_UNAVAILABLE" ? AiUsageStatus.ProviderUnavailable : AiUsageStatus.Failed,
                Model: model, PromptTokens: response.PromptTokens ?? 0, CompletionTokens: response.CompletionTokens ?? 0,
                ErrorMessage: response.ErrorMessage, DurationMs: durationMs, UserId: input.UserId), cancellationToken);

        // Không đoán điểm khi AI lỗi / trả kết quả không đọc được: báo lỗi thay vì "đạt" giả
        if (!response.Succeeded || response.Score is not { } score)
            throw new InvalidOperationException(response.ErrorMessage ?? "AI Vision không trả được kết quả, thử lại sau.");

        var deviations = response.Deviations.Select(d => new ImageConsistencyDeviationDto(d.Aspect, d.Description, d.RegionHint)).ToList();
        var summaryReason = string.IsNullOrWhiteSpace(response.SummaryReason) ? "Khớp với tạo hình nhân vật." : response.SummaryReason;
        var passed = score >= threshold;
        Guid? findingId = passed ? null
            : await RecordConsistencyFindingAsync(input, score, threshold, deviations, summaryReason, cancellationToken);

        return new ImageConsistencyResultDto(
            PanelId: input.PanelId,
            CharacterId: input.CharacterId,
            CharacterName: input.CharacterName,
            CharacterSheetVersion: input.CharacterVersion,
            Score: score,
            Threshold: threshold,
            Passed: passed,
            SummaryReason: summaryReason,
            Deviations: deviations,
            FindingId: findingId,
            RevisionRef: input.RevisionRef,
            AssetReferenceId: input.AssetReferenceId
        );
    }

    private async Task<Guid> RecordConsistencyFindingAsync(
        ImageConsistencyInput input,
        decimal score,
        decimal threshold,
        IReadOnlyList<ImageConsistencyDeviationDto> deviations,
        string summaryReason,
        CancellationToken cancellationToken)
    {
        var panel = await _db.Panels.AsNoTracking().FirstOrDefaultAsync(p => p.Id == input.PanelId, cancellationToken);
        var page = panel != null ? await _db.Pages.AsNoTracking().FirstOrDefaultAsync(p => p.Id == panel.PageId, cancellationToken) : null;

        // Vision chỉ cảnh báo, không chặn duyệt chương: kết luận của model có thể sai, người quyết định qua Review
        var severity = ConsistencyFindingSeverity.Warning;

        var message = $"Ngoại hình nhân vật '{input.CharacterName}' trong Khung {panel?.PanelNumber ?? 1} (Trang {page?.PageNumber ?? 1}) có độ lệch hình ảnh ({score:P0} < {threshold:P0}) so với Character Sheet khóa v{input.CharacterVersion}: {summaryReason}";

        var evidenceObj = new
        {
            characterId = input.CharacterId,
            characterName = input.CharacterName,
            characterSheetVersion = input.CharacterVersion,
            panelId = input.PanelId,
            panelNumber = panel?.PanelNumber ?? 1,
            pageId = page?.Id,
            pageNumber = page?.PageNumber ?? 1,
            assetReferenceId = input.AssetReferenceId,
            revisionRef = input.RevisionRef,
            similarityScore = score,
            threshold = threshold,
            summaryReason = summaryReason,
            deviations = deviations.Select(d => new
            {
                aspect = d.Aspect,
                description = d.Description,
                regionHint = d.RegionHint
            }).ToList()
        };

        var jsonOptions = new JsonSerializerOptions
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
        var evidenceJson = JsonSerializer.Serialize(evidenceObj, jsonOptions);
        var fingerprint = ComputeFingerprint(input.ChapterId, input.PanelId, input.CharacterId, input.RevisionRef);

        var existingFinding = await _db.ConsistencyFindings
            .FirstOrDefaultAsync(f => f.ChapterId == input.ChapterId && f.Fingerprint == fingerprint, cancellationToken);

        if (existingFinding is not null)
        {
            existingFinding.Redetected(DateTime.UtcNow);
            await _db.SaveChangesAsync(cancellationToken);
            return existingFinding.Id;
        }

        var finding = ConsistencyFinding.Create(
            chapterId: input.ChapterId,
            ruleCode: ConsistencyChecker.ImageInconsistency,
            severity: severity,
            message: message,
            fingerprint: fingerprint,
            evidence: evidenceJson,
            sceneId: input.PanelId,
            bibleEntryId: input.CharacterId
        );

        if (input.UserId.HasValue)
        {
            finding.CreatedBy = input.UserId.Value.ToString();
        }

        _db.ConsistencyFindings.Add(finding);
        await _db.SaveChangesAsync(cancellationToken);
        return finding.Id;
    }

    private static string ComputeFingerprint(Guid chapterId, Guid panelId, Guid characterId, string? revisionRef)
    {
        var raw = $"IMAGE_INCONSISTENCY|{chapterId}|{panelId}|{characterId}|{revisionRef}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
    }

    private async Task<byte[]?> ReadAssetBytesAsync(string storageKey, CancellationToken cancellationToken)
    {
        try
        {
            using var stream = await _storage.OpenReadAsync(storageKey, cancellationToken);
            if (stream is null) return null;

            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms, cancellationToken);
            return ms.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Không đọc được file từ storage theo key {Key}", storageKey);
            return null;
        }
    }

    private async Task<byte[]?> ResolveImageBytesAsync(string url, CancellationToken cancellationToken)
    {
        // Nếu là Data URL
        if (url.StartsWith("data:image", StringComparison.OrdinalIgnoreCase))
        {
            var commaIdx = url.IndexOf(',');
            if (commaIdx >= 0)
            {
                return Convert.FromBase64String(url[(commaIdx + 1)..]);
            }
        }

        // Nếu URL chứa Asset ID Guid
        var parts = url.Split('/', StringSplitOptions.RemoveEmptyEntries);
        foreach (var p in parts)
        {
            if (Guid.TryParse(p, out var assetId))
            {
                var asset = await _db.Assets.AsNoTracking().FirstOrDefaultAsync(a => a.Id == assetId, cancellationToken);
                if (asset is not null)
                {
                    return await ReadAssetBytesAsync(asset.StorageKey, cancellationToken);
                }
            }
        }

        return null;
    }

    private static string SniffMime(byte[] b) => b switch
    {
        [0xFF, 0xD8, ..] => "image/jpeg",
        [0x52, 0x49, 0x46, 0x46, _, _, _, _, 0x57, 0x45, 0x42, 0x50, ..] => "image/webp",
        _ => "image/png"
    };

    private static ImageConsistencyResultDto Skipped(Guid panelId, decimal threshold, string reason, string? revisionRef) =>
        new(panelId, null, null, null, 0m, threshold, true, reason, Array.Empty<ImageConsistencyDeviationDto>(),
            RevisionRef: revisionRef, Skipped: true);
}
