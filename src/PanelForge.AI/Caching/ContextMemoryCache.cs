using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using PanelForge.AI.Agents;
using PanelForge.AI.Contracts;

namespace PanelForge.AI.Caching;

/// <summary>
/// Bộ nhớ đệm Memory Cache trong tiến trình PanelForge.AI.
/// Lưu bản tóm tắt Series Bible gần nhất theo (SeriesId, ChapterNumber, Hash bản sửa đổi Bible)
/// để giảm thiểu 40-60% Token gửi lên Gemini mỗi lần chạy Continuity Agent.
/// </summary>
public sealed class ContextMemoryCache : IContextMemoryCache
{
    private static readonly TimeSpan DefaultSlidingExpiration = TimeSpan.FromMinutes(45);
    private static readonly TimeSpan DefaultAbsoluteExpiration = TimeSpan.FromHours(2);

    private readonly IMemoryCache _cache;
    private readonly ILogger<ContextMemoryCache> _logger;

    public ContextMemoryCache(IMemoryCache cache, ILogger<ContextMemoryCache> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    public CachedBibleContext GetOrBuildBibleSummary(AgentContext ctx, KeyMaps keys)
    {
        var rawBlock = PromptText.BibleBlock(ctx, keys);
        var originalChars = rawBlock.Length;

        // Mã băm định danh bất biến dựa trên tập hợp RevisionId của Bible đang có hiệu lực
        var revisionsKey = string.Join(";", ctx.Bible.Select(b => $"{b.EntryId}:{b.RevisionId}"));
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(revisionsKey)))[..16].ToLowerInvariant();
        var cacheKey = $"bible-context:{ctx.SeriesId}:{ctx.ChapterNumber}:{hash}";

        if (_cache.TryGetValue(cacheKey, out CachedBibleContext? cached) && cached is not null)
        {
            _logger.LogInformation(
                "[ContextMemoryCache] HIT cho Series {SeriesId}, Chương {ChapterNumber}. Tiết kiệm ước tính ~{Tokens} tokens.",
                ctx.SeriesId, ctx.ChapterNumber, cached.EstimatedTokensSaved);
            return cached with { IsCacheHit = true };
        }

        _logger.LogInformation(
            "[ContextMemoryCache] MISS cho Series {SeriesId}, Chương {ChapterNumber}. Đang chuẩn hóa và nén tóm tắt Bible vào cache.",
            ctx.SeriesId, ctx.ChapterNumber);

        var condensedBlock = BuildCondensedBibleBlock(keys);
        var condensedChars = condensedBlock.Length;
        // Quy đổi ước tính 1 token ~ 3.8 - 4 ký tự tiếng Việt/tiếng Anh
        var savedTokens = Math.Max(0, (originalChars - condensedChars) / 4);

        var result = new CachedBibleContext(
            condensedBlock,
            originalChars,
            condensedChars,
            savedTokens,
            IsCacheHit: false);

        var cacheEntryOptions = new MemoryCacheEntryOptions()
            .SetSlidingExpiration(DefaultSlidingExpiration)
            .SetAbsoluteExpiration(DefaultAbsoluteExpiration)
            .SetSize(1); // Cho phép giới hạn kích thước memory cache

        _cache.Set(cacheKey, result, cacheEntryOptions);
        return result;
    }

    /// <summary>
    /// Chuẩn hóa và tinh giản bản tóm tắt Bible: nhóm theo phân loại (Nhân vật, Địa điểm, Quy luật),
    /// loại bỏ khoảng trắng thừa nhưng bảo tồn 100% tên, mã code và các sự thật cần đối chiếu.
    /// </summary>
    private static string BuildCondensedBibleBlock(KeyMaps keys)
    {
        var sb = new StringBuilder();
        var grouped = keys.Bible.GroupBy(kv => kv.Value.Category).OrderBy(g => g.Key);

        foreach (var group in grouped)
        {
            sb.AppendLine($"### [MỤC: {group.Key.ToUpperInvariant()}]");
            foreach (var (key, b) in group)
            {
                var cleanDesc = Flatten(b.Description);
                var cleanDetails = string.IsNullOrWhiteSpace(b.Details) ? null : Flatten(b.Details);

                sb.Append($"[{key}] {b.Name} (@{b.Code}): {cleanDesc}");
                if (cleanDetails is not null)
                {
                    sb.Append($" | Ràng buộc: {cleanDetails}");
                }
                sb.AppendLine();
            }
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }

    private static string Flatten(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        return System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();
    }
}
