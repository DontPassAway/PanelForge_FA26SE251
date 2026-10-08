using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Consistency.Common;

public sealed record CheckLine(
    Guid Id, Guid SceneId, int SceneNumber, int LineOrder, string? Dialogue, string? StageDirection,
    Guid? SpeakerId, IReadOnlyList<Guid> LinkedEntryIds);

/// <param name="FirstEffectiveChapter">Chương nhỏ nhất mà mục có revision hiệu lực (BR-15); null = chưa có revision.</param>
public sealed record CheckEntry(Guid Id, string Name, BibleEntryCategory Category, bool IsActive, int? FirstEffectiveChapter);

public sealed record CheckRule(Guid Id, string Name, string RuleType, string? Pattern, string Severity, string ConfigurationJson);

public sealed record DetectedFinding(
    string RuleCode, ConsistencyFindingSeverity Severity, string Message, string? Evidence,
    Guid? SceneId, Guid? ScriptLineId, Guid? BibleEntryId, Guid? ConsistencyRuleId)
{
    /// <summary>Định danh "cùng một lỗi" qua các lần chạy: đổi chữ vi phạm / đổi luật thì là lỗi mới.</summary>
    public string Fingerprint => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        $"{RuleCode}|{ConsistencyRuleId}|{ScriptLineId}|{BibleEntryId}|{Evidence}"))).ToLowerInvariant();
}

/// <summary>
/// Tầng 1 của kiểm tra nhất quán (CF3): luật tất định bằng code trên script của một chương, so với Series Bible
/// có hiệu lực ở chương đó và các ConsistencyRule đang bật. Không đọc hiểu văn bản tự do (việc của tầng LLM).
/// </summary>
public static class ConsistencyChecker
{
    public const string SpeakerMissing = "SPEAKER_MISSING";
    public const string SpeakerUnknown = "SPEAKER_UNKNOWN";
    public const string SpeakerNotCharacter = "SPEAKER_NOT_CHARACTER";
    public const string EntryInactive = "ENTRY_INACTIVE";
    public const string EntryNotYetEffective = "ENTRY_NOT_YET_EFFECTIVE";
    public const string RulePattern = "RULE_PATTERN";
    public const string DialogueTooLong = "DIALOGUE_TOO_LONG";

    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(200);

    public static List<DetectedFinding> Check(
        decimal chapterNumber, IReadOnlyList<CheckLine> lines, IReadOnlyDictionary<Guid, CheckEntry> entries,
        IReadOnlyList<CheckRule> rules)
    {
        var found = new List<DetectedFinding>();
        foreach (var line in lines.OrderBy(l => l.SceneNumber).ThenBy(l => l.LineOrder))
        {
            var where = $"Cảnh {line.SceneNumber}, dòng {line.LineOrder}";
            var text = Quote(line);

            if (!string.IsNullOrWhiteSpace(line.Dialogue) && line.SpeakerId is null)
                found.Add(new(SpeakerMissing, ConsistencyFindingSeverity.Warning,
                    $"{where}: lời thoại chưa có nhân vật nói.", text, line.SceneId, line.Id, null, null));

            if (line.SpeakerId is { } speakerId)
            {
                if (!entries.TryGetValue(speakerId, out var speaker))
                    found.Add(new(SpeakerUnknown, ConsistencyFindingSeverity.Blocking,
                        $"{where}: nhân vật nói không có trong Series Bible.", text, line.SceneId, line.Id, speakerId, null));
                else if (speaker.Category != BibleEntryCategory.Character)
                    found.Add(new(SpeakerNotCharacter, ConsistencyFindingSeverity.Blocking,
                        $"{where}: \"{speaker.Name}\" là {speaker.Category}, không phải nhân vật nên không thể nói.",
                        text, line.SceneId, line.Id, speakerId, null));
            }

            // Mục Bible được dòng nhắc tới (người nói + liên kết): phải còn dùng và đã có hiệu lực ở chương này
            var mentioned = line.SpeakerId is { } s ? line.LinkedEntryIds.Append(s) : line.LinkedEntryIds;
            foreach (var entryId in mentioned.Distinct())
            {
                if (!entries.TryGetValue(entryId, out var entry)) continue;
                if (!entry.IsActive)
                    found.Add(new(EntryInactive, ConsistencyFindingSeverity.Warning,
                        $"{where}: dùng mục Bible \"{entry.Name}\" đã ngừng sử dụng.", text, line.SceneId, line.Id, entryId, null));
                if (entry.FirstEffectiveChapter is { } first && first > chapterNumber)
                    found.Add(new(EntryNotYetEffective, ConsistencyFindingSeverity.Blocking,
                        $"{where}: \"{entry.Name}\" chỉ có hiệu lực từ chương {first}, chưa xuất hiện ở chương {chapterNumber}.",
                        text, line.SceneId, line.Id, entryId, null));
            }

            foreach (var rule in rules)
                if (CheckRuleOnLine(rule, line, where, text) is { } hit) found.Add(hit);
        }
        return found;
    }

    /// <summary>
    /// Luật Producer cấu hình: có <c>Pattern</c> (regex, không phân biệt hoa thường) = chữ bị cấm, khớp trong thoại hoặc
    /// chỉ dẫn là vi phạm; RuleType <c>DialogueOverflow</c> với ConfigurationJson <c>{"maxChars": N}</c> = thoại dài quá N ký tự.
    /// </summary>
    private static DetectedFinding? CheckRuleOnLine(CheckRule rule, CheckLine line, string where, string text)
    {
        var severity = rule.Severity.Trim().ToLowerInvariant() switch
        {
            "error" or "blocking" => ConsistencyFindingSeverity.Blocking,
            "info" => ConsistencyFindingSeverity.Info,
            _ => ConsistencyFindingSeverity.Warning
        };

        if (!string.IsNullOrWhiteSpace(rule.Pattern))
        {
            Match match;
            try
            {
                match = Regex.Match($"{line.Dialogue}\n{line.StageDirection}", rule.Pattern,
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout);
            }
            catch (Exception ex) when (ex is ArgumentException or RegexMatchTimeoutException)
            {
                return null; // regex hỏng / quá chậm: bỏ qua luật này, không chặn cả lượt kiểm tra
            }
            if (match.Success)
                return new(RulePattern, severity, $"{where}: vi phạm luật \"{rule.Name}\" (\"{match.Value}\").",
                    text, line.SceneId, line.Id, null, rule.Id);
        }

        if (string.Equals(rule.RuleType, "DialogueOverflow", StringComparison.OrdinalIgnoreCase)
            && MaxChars(rule.ConfigurationJson) is { } max && (line.Dialogue?.Length ?? 0) > max)
            return new(DialogueTooLong, severity,
                $"{where}: thoại dài {line.Dialogue!.Length} ký tự, vượt {max} của luật \"{rule.Name}\".",
                text, line.SceneId, line.Id, null, rule.Id);

        return null;
    }

    private static int? MaxChars(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
            return doc.RootElement.ValueKind == JsonValueKind.Object &&
                   doc.RootElement.TryGetProperty("maxChars", out var p) && p.TryGetInt32(out var v) && v > 0 ? v : null;
        }
        catch (JsonException) { return null; }
    }

    private static string Quote(CheckLine l) =>
        string.Join(" / ", new[] { l.Dialogue, l.StageDirection }.Where(s => !string.IsNullOrWhiteSpace(s)));
}
