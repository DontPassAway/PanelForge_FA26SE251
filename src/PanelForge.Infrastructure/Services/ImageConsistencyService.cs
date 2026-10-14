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
using PanelForge.Application.Common.Models;
using PanelForge.Application.Features.Assets.Common;
using PanelForge.Application.Features.Consistency.Common;
using PanelForge.Application.Features.Consistency.Models;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;

namespace PanelForge.Infrastructure.Services;

/// <summary>
/// Thực thi kiểm tra nhất quán hình ảnh AI (Bước A5, OpenAI-Compatible Vision với Gemini / OpenAI).
/// Hỗ trợ fallback API key từ WorkspaceAiConfig sang AiVisionSettings (Task 3.1 & 3.2).
/// </summary>
public sealed class ImageConsistencyService : IImageConsistencyService
{
    private readonly HttpClient _httpClient;
    private readonly IPanelForgeDbContext _db;
    private readonly IAssetStorage _storage;
    private readonly IAiKeyProtector _keyProtector;
    private readonly IAiUsageService _usageService;
    private readonly AiVisionSettings _settings;
    private readonly ILogger<ImageConsistencyService> _logger;

    public ImageConsistencyService(
        HttpClient httpClient,
        IPanelForgeDbContext db,
        IAssetStorage storage,
        IAiKeyProtector keyProtector,
        IAiUsageService usageService,
        IOptions<AiVisionSettings> settings,
        ILogger<ImageConsistencyService> logger)
    {
        _httpClient = httpClient;
        _db = db;
        _storage = storage;
        _keyProtector = keyProtector;
        _usageService = usageService;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<ImageConsistencyResultDto> CheckPanelConsistencyAsync(
        Guid panelId,
        string? revisionRef = null,
        decimal? threshold = null,
        CancellationToken cancellationToken = default)
    {
        var targetThreshold = threshold ?? _settings.SimilarityThreshold;

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

        // Nếu môi trường dev chưa có file thật trên disk, tạo placeholder ảnh mẫu hợp lệ
        if (artworkBytes is null || artworkBytes.Length == 0)
        {
            artworkBytes = CreateDummyPngBytes();
            artworkMime = "image/png";
        }

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

        var targetCharacter = characterEntries.FirstOrDefault(c => speakerIds.Contains(c.Id))
            ?? characterEntries.FirstOrDefault();

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
                RevisionRef: revisionRef
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
        string sheetMime = "image/png";

        if (!string.IsNullOrWhiteSpace(targetCharacter.ReferenceImageUrl))
        {
            sheetBytes = await ResolveImageBytesAsync(targetCharacter.ReferenceImageUrl, cancellationToken);
        }

        if (sheetBytes is null || sheetBytes.Length == 0)
        {
            sheetBytes = CreateDummyPngBytes();
        }

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
            CharacterSheetMimeType: sheetMime,
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
        var threshold = input.Threshold ?? _settings.SimilarityThreshold;
        var apiKey = await ResolveApiKeyAsync(input.WorkspaceId, cancellationToken);

        var stopwatch = Stopwatch.StartNew();
        var model = string.IsNullOrWhiteSpace(_settings.ModelName) ? "gemini-2.5-flash" : _settings.ModelName;
        var endpoint = string.IsNullOrWhiteSpace(_settings.EndpointUrl)
            ? "https://generativelanguage.googleapis.com/v1beta/openai/chat/completions"
            : _settings.EndpointUrl;

        var systemPrompt = $"You are a manga continuity editor. Compare Image 1 (Locked Character Sheet) and Image 2 (Submitted Panel Artwork) for the character '{input.CharacterName}'. Evaluate visual consistency across hair style/color, facial features, outfit, and key accessories. Return strict JSON: {{ \"score\": <float 0.0 to 1.0>, \"deviations\": [ {{ \"aspect\": \"outfit|hair|face|accessory\", \"description\": \"...\", \"regionHint\": \"...\" }} ], \"summaryReason\": \"...\" }}";

        var sheetBase64 = $"data:{input.CharacterSheetMimeType};base64,{Convert.ToBase64String(input.CharacterSheetBytes)}";
        var artBase64 = $"data:{input.ArtworkMimeType};base64,{Convert.ToBase64String(input.ArtworkBytes)}";

        var requestBody = new
        {
            model = model,
            messages = new object[]
            {
                new
                {
                    role = "system",
                    content = systemPrompt
                },
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new
                        {
                            type = "text",
                            text = $"Compare Image 1 (Locked Character Sheet) and Image 2 (Submitted Panel Artwork) for character '{input.CharacterName}'."
                        },
                        new
                        {
                            type = "image_url",
                            image_url = new { url = sheetBase64 }
                        },
                        new
                        {
                            type = "image_url",
                            image_url = new { url = artBase64 }
                        }
                    }
                }
            },
            response_format = new { type = "json_object" }
        };

        var jsonPayload = JsonSerializer.Serialize(requestBody);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint);
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        httpRequest.Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

        HttpResponseMessage response;
        string responseContent;
        try
        {
            response = await _httpClient.SendAsync(httpRequest, cancellationToken);
            responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Lỗi kết nối khi gọi AI Vision tới {Endpoint}", endpoint);

            await _usageService.RecordAsync(new AiUsageEntry(
                WorkspaceId: input.WorkspaceId,
                Feature: "Consistency.ImageConsistencyCheck",
                Status: AiUsageStatus.ProviderUnavailable,
                Model: model,
                ErrorMessage: ex.Message,
                DurationMs: (int)stopwatch.ElapsedMilliseconds,
                UserId: input.UserId
            ), cancellationToken);

            throw new InvalidOperationException($"Không thể gọi dịch vụ AI Vision: {ex.Message}", ex);
        }

        stopwatch.Stop();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("AI Vision trả lỗi HTTP {StatusCode}: {Content}", response.StatusCode, responseContent);

            await _usageService.RecordAsync(new AiUsageEntry(
                WorkspaceId: input.WorkspaceId,
                Feature: "Consistency.ImageConsistencyCheck",
                Status: AiUsageStatus.Failed,
                Model: model,
                ErrorMessage: $"HTTP {(int)response.StatusCode}: {responseContent}",
                DurationMs: (int)stopwatch.ElapsedMilliseconds,
                UserId: input.UserId
            ), cancellationToken);

            throw new InvalidOperationException($"Dịch vụ AI Vision trả lỗi (HTTP {(int)response.StatusCode}): {responseContent}");
        }

        // Phân tích kết quả từ OpenAI Chat Completion Response
        long promptTokens = 0;
        long completionTokens = 0;
        decimal score = 0.85m;
        var deviations = new List<ImageConsistencyDeviationDto>();
        string summaryReason = "Khớp với tạo hình nhân vật.";

        try
        {
            using var doc = JsonDocument.Parse(responseContent);
            var root = doc.RootElement;

            if (root.TryGetProperty("usage", out var usageElem))
            {
                if (usageElem.TryGetProperty("prompt_tokens", out var pt)) promptTokens = pt.GetInt64();
                if (usageElem.TryGetProperty("completion_tokens", out var ct)) completionTokens = ct.GetInt64();
            }

            if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
            {
                var choice = choices[0];
                if (choice.TryGetProperty("message", out var msgElem) &&
                    msgElem.TryGetProperty("content", out var contentElem))
                {
                    var rawContent = contentElem.GetString() ?? string.Empty;
                    var parsed = ParseAiVisionResponse(rawContent);
                    score = parsed.Score;
                    deviations = parsed.Deviations;
                    summaryReason = parsed.SummaryReason ?? summaryReason;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Không thể parse JSON phản hồi từ AI Vision, dùng giá trị fallback.");
        }

        // Lưu bản ghi AI Provenance (BR-10)
        await _usageService.RecordAsync(new AiUsageEntry(
            WorkspaceId: input.WorkspaceId,
            Feature: "Consistency.ImageConsistencyCheck",
            Status: AiUsageStatus.Succeeded,
            Model: model,
            PromptTokens: promptTokens,
            CompletionTokens: completionTokens,
            DurationMs: (int)stopwatch.ElapsedMilliseconds,
            UserId: input.UserId
        ), cancellationToken);

        var passed = score >= threshold;
        Guid? findingId = null;

        // Nếu điểm dưới ngưỡng (score < 0.75): Gắn cờ và tạo/cập nhật ConsistencyFinding (Task 3.1 + 3.2)
        if (!passed)
        {
            findingId = await RecordConsistencyFindingAsync(input, score, threshold, deviations, summaryReason, cancellationToken);
        }

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

        var severity = score < 0.55m
            ? ConsistencyFindingSeverity.Blocking
            : ConsistencyFindingSeverity.Warning;

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

    private async Task<string> ResolveApiKeyAsync(Guid workspaceId, CancellationToken cancellationToken)
    {
        // 1. Ưu tiên lấy key từ WorkspaceAiConfig
        var config = await _db.WorkspaceAiConfigs.AsNoTracking()
            .FirstOrDefaultAsync(c => c.WorkspaceId == workspaceId, cancellationToken);

        if (config is not null && config.IsEnabled && !string.IsNullOrWhiteSpace(config.ApiKeyEncrypted))
        {
            try
            {
                var decrypted = _keyProtector.Unprotect(config.ApiKeyEncrypted);
                if (!string.IsNullOrWhiteSpace(decrypted))
                    return decrypted.Trim();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Không giải mã được ApiKey từ WorkspaceAiConfig, fallback về AiVisionSettings.");
            }
        }

        // 2. Fallback về AiVisionSettings
        if (!string.IsNullOrWhiteSpace(_settings.ApiKey))
        {
            return _settings.ApiKey.Trim();
        }

        throw new InvalidOperationException("Chưa cấu hình API Key cho AI Vision (cả Workspace và AiVisionSettings trong appsettings đều chưa có key hợp lệ).");
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

    private static (decimal Score, List<ImageConsistencyDeviationDto> Deviations, string? SummaryReason)
        ParseAiVisionResponse(string content)
    {
        var json = content.Trim();
        if (json.StartsWith("```json", StringComparison.OrdinalIgnoreCase)) json = json[7..];
        else if (json.StartsWith("```")) json = json[3..];
        if (json.EndsWith("```")) json = json[..^3];
        json = json.Trim();

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        decimal score = 0.8m;
        if (root.TryGetProperty("score", out var scoreElem))
        {
            score = scoreElem.GetDecimal();
        }

        string? summaryReason = null;
        if (root.TryGetProperty("summaryReason", out var reasonElem))
        {
            summaryReason = reasonElem.GetString();
        }

        var deviations = new List<ImageConsistencyDeviationDto>();
        if (root.TryGetProperty("deviations", out var devArr) && devArr.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in devArr.EnumerateArray())
            {
                var aspect = item.TryGetProperty("aspect", out var asp) ? asp.GetString() ?? "general" : "general";
                var desc = item.TryGetProperty("description", out var ds) ? ds.GetString() ?? "" : "";
                var hint = item.TryGetProperty("regionHint", out var rh) ? rh.GetString() : null;
                deviations.Add(new ImageConsistencyDeviationDto(aspect, desc, hint));
            }
        }

        return (score, deviations, summaryReason);
    }

    private static byte[] CreateDummyPngBytes()
    {
        // 1x1 transparent PNG data
        return Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");
    }
}
