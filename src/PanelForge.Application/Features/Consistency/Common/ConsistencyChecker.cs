using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Consistency.Common;

public sealed record CheckLine(
    Guid Id, Guid SceneId, int SceneNumber, int LineOrder, string? Dialogue, string? StageDirection,
    Guid? SpeakerId, IReadOnlyList<Guid> LinkedEntryIds,
    Guid? PanelId = null, int? PanelReadingOrder = null, int? PageNumber = null,
    NarrationMode NarrationMode = NarrationMode.Present);

public sealed record CheckKnownBy(string? Character, Guid? CharacterId, int FromChapter);

/// <param name="FirstEffectiveChapter">Chương nhỏ nhất mà mục có revision hiệu lực (BR-15); null = chưa có revision.</param>
/// <param name="FirstLockedChapter">Chương nhỏ nhất mà mục có revision đã khoá (V1.2 character sheet); null = chưa khoá bản nào.</param>
public sealed record CheckEntry
{
    public Guid Id { get; init; }
    public string Name { get; init; }
    public BibleEntryCategory Category { get; init; }
    public bool IsActive { get; init; }
    public int? FirstEffectiveChapter { get; init; }
    public int? FirstLockedChapter { get; init; }
    public string? DetailsJson { get; init; }
    public int? DiesAtChapter { get; init; }
    public int? RevivesAtChapter { get; init; }
    public int? AvailableFromChapter { get; init; }
    public IReadOnlyList<CheckKnownBy>? KnownBy { get; init; }
    public string? Code { get; init; }
    public int? RevealedFromChapter { get; init; }

    public CheckEntry(
        Guid id, string name, BibleEntryCategory category, bool isActive,
        int? firstEffectiveChapter, int? firstLockedChapter = null,
        string? detailsJson = null, int? diesAtChapter = null, int? revivesAtChapter = null,
        int? availableFromChapter = null, IReadOnlyList<CheckKnownBy>? knownBy = null,
        string? code = null, int? revealedFromChapter = null)
    {
        Id = id;
        Name = name;
        Category = category;
        IsActive = isActive;
        FirstEffectiveChapter = firstEffectiveChapter;
        FirstLockedChapter = firstLockedChapter;
        DetailsJson = detailsJson;
        Code = code;

        int? parsedDies = diesAtChapter;
        int? parsedRevives = revivesAtChapter;
        int? parsedAvailable = availableFromChapter;
        int? parsedRevealed = revealedFromChapter;
        List<CheckKnownBy>? parsedKnownBy = knownBy != null ? new List<CheckKnownBy>(knownBy) : null;

        if (!string.IsNullOrWhiteSpace(detailsJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(detailsJson);
                var root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Object)
                {
                    if (parsedDies == null)
                    {
                        if (TryGetInt(root, "diesAtChapter", out var d) ||
                            TryGetInt(root, "deathChapter", out d) ||
                            TryGetInt(root, "diesAt", out d))
                            parsedDies = d;
                    }
                    if (parsedRevives == null)
                    {
                        if (TryGetInt(root, "revivesAtChapter", out var r) ||
                            TryGetInt(root, "revivalChapter", out r) ||
                            TryGetInt(root, "revivesAt", out r))
                            parsedRevives = r;
                    }
                    if (parsedAvailable == null)
                    {
                        if (TryGetInt(root, "availableFromChapter", out var a) ||
                            TryGetInt(root, "notBeforeChapter", out a) ||
                            TryGetInt(root, "availableFrom", out a))
                            parsedAvailable = a;
                    }
                    if (parsedRevealed == null)
                    {
                        if (TryGetInt(root, "revealedToReaderFromChapter", out var rev) ||
                            TryGetInt(root, "revealedFromChapter", out rev) ||
                            TryGetInt(root, "knownFromChapter", out rev))
                            parsedRevealed = rev;
                    }
                    if (parsedKnownBy == null && (root.TryGetProperty("knownBy", out var kb) || root.TryGetProperty("KnownBy", out kb)) && kb.ValueKind == JsonValueKind.Array)
                    {
                        parsedKnownBy = new List<CheckKnownBy>();
                        foreach (var item in kb.EnumerateArray())
                        {
                            if (item.ValueKind == JsonValueKind.Object)
                            {
                                string? charName = null;
                                Guid? charId = null;
                                int fromCh = 1;
                                if (item.TryGetProperty("character", out var c) || item.TryGetProperty("Character", out c))
                                    charName = c.GetString();
                                if ((item.TryGetProperty("characterId", out var ci) || item.TryGetProperty("CharacterId", out ci)) && ci.TryGetGuid(out var g))
                                    charId = g;
                                if (TryGetInt(item, "fromChapter", out var fc) || TryGetInt(item, "FromChapter", out fc))
                                    fromCh = fc;
                                parsedKnownBy.Add(new CheckKnownBy(charName, charId, fromCh));
                            }
                        }
                    }
                }
            }
            catch (JsonException) { }
        }

        DiesAtChapter = parsedDies;
        RevivesAtChapter = parsedRevives;
        AvailableFromChapter = parsedAvailable;
        RevealedFromChapter = parsedRevealed;
        KnownBy = parsedKnownBy;
    }

    private static bool TryGetInt(JsonElement el, string propName, out int value)
    {
        value = 0;
        return (el.TryGetProperty(propName, out var p) ||
                (propName.Length > 0 && el.TryGetProperty(char.ToUpperInvariant(propName[0]) + propName[1..], out p)))
               && p.TryGetInt32(out value);
    }
}

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
    public const string CharacterNotLocked = "CHARACTER_NOT_LOCKED";
    public const string RulePattern = "RULE_PATTERN";
    public const string DialogueTooLong = "DIALOGUE_TOO_LONG";
    public const string CharacterAfterDeath = "CHARACTER_AFTER_DEATH";
    public const string PropNotBefore = "PROP_NOT_BEFORE";
    public const string KnowledgeBeforeReveal = "KNOWLEDGE_BEFORE_REVEAL";
    public const string ReadingOrderWrong = "READING_ORDER_WRONG";
    public const string VisualCharacterDeviation = "VISUAL_CHARACTER_DEVIATION";
    public const string ImageInconsistency = "IMAGE_INCONSISTENCY";
    public const decimal DefaultVisualSimilarityThreshold = 0.75m;

    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(200);

    public static List<DetectedFinding> Check(
        decimal chapterNumber, IReadOnlyList<CheckLine> lines, IReadOnlyDictionary<Guid, CheckEntry> entries,
        IReadOnlyList<CheckRule> rules)
    {
        var found = new List<DetectedFinding>();
        var notLockedReported = new HashSet<Guid>();
        CheckLine? prevPanelLine = null;

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
                else
                {
                    // CHARACTER_AFTER_DEATH: nhân vật đã chết từ chương X vẫn xuất hiện hay nói
                    // Ngoại lệ có chủ ý (Flashback / Dream / Imagination): không phạt vì là hồi tưởng quá khứ hoặc giấc mơ
                    if (line.NarrationMode is not (NarrationMode.Flashback or NarrationMode.Dream or NarrationMode.Imagination))
                    {
                        if (speaker.DiesAtChapter is { } diesAt && chapterNumber > diesAt
                            && !(speaker.RevivesAtChapter is { } revives && chapterNumber >= revives))
                        {
                            var sev = line.NarrationMode == NarrationMode.UnreliableNarrator
                                ? ConsistencyFindingSeverity.Warning
                                : ConsistencyFindingSeverity.Blocking;

                            found.Add(new(CharacterAfterDeath, sev,
                                $"{where}: nhân vật \"{speaker.Name}\" đã chết từ chương {diesAt}, không thể xuất hiện hay nói ở chương {chapterNumber}.",
                                text, line.SceneId, line.Id, speakerId, null));
                        }
                    }
                }
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
                // Nhân vật đã có hiệu lực nhưng chưa có bản khoá: AI không dùng làm tham chiếu. Báo một lần mỗi nhân vật/chương.
                if (entry.Category == BibleEntryCategory.Character && entry.FirstEffectiveChapter <= chapterNumber
                    && !(entry.FirstLockedChapter <= chapterNumber) && notLockedReported.Add(entryId))
                    found.Add(new(CharacterNotLocked, ConsistencyFindingSeverity.Warning,
                        $"\"{entry.Name}\" chưa có character sheet được Producer khoá ở chương {chapterNumber}: AI sẽ không dùng nhân vật này làm tham chiếu.",
                        entry.Name, null, null, entryId, null));
            }

            // PROP_NOT_BEFORE & KNOWLEDGE_BEFORE_REVEAL trên các liên kết kịch bản (LinkedEntryIds)
            foreach (var entryId in line.LinkedEntryIds.Distinct())
            {
                if (!entries.TryGetValue(entryId, out var entry)) continue;

                // PROP_NOT_BEFORE: Đạo cụ/sự kiện không được xuất hiện trước chương X
                // Ngoại lệ có chủ ý (Dream / Imagination): giấc mơ không làm đạo cụ thực sự xuất hiện trong mạch truyện
                if (line.NarrationMode is not (NarrationMode.Dream or NarrationMode.Imagination))
                {
                    if (entry.AvailableFromChapter is { } notBefore && chapterNumber < notBefore)
                    {
                        var sev = line.NarrationMode == NarrationMode.UnreliableNarrator
                            ? ConsistencyFindingSeverity.Warning
                            : ConsistencyFindingSeverity.Blocking;

                        found.Add(new(PropNotBefore, sev,
                            $"{where}: đạo cụ/sự kiện \"{entry.Name}\" không được xuất hiện trước chương {notBefore} (chương hiện tại: {chapterNumber}).",
                            text, line.SceneId, line.Id, entryId, null));
                    }
                }

                // KNOWLEDGE_BEFORE_REVEAL: Nhân vật nhắc điều chưa được biết
                // Ngoại lệ có chủ ý (Dream / Imagination / Lie / Rumor):
                // Giấc mơ không làm nhân vật biết thật; Lời nói dối hoặc tin đồn là tuyên bố của nhân vật, không phải sự thật thế giới
                if (line.NarrationMode is not (NarrationMode.Dream or NarrationMode.Imagination or NarrationMode.Lie or NarrationMode.Rumor))
                {
                    if (line.SpeakerId is { } spkId && entries.TryGetValue(spkId, out var speaker))
                    {
                        var sev = line.NarrationMode == NarrationMode.UnreliableNarrator
                            ? ConsistencyFindingSeverity.Warning
                            : ConsistencyFindingSeverity.Blocking;

                        if (entry.KnownBy is { Count: > 0 } knownList)
                        {
                            var ruleForSpeaker = knownList.FirstOrDefault(k =>
                                (k.CharacterId.HasValue && k.CharacterId == speaker.Id) ||
                                (!string.IsNullOrWhiteSpace(k.Character) &&
                                 (string.Equals(k.Character, speaker.Name, StringComparison.OrdinalIgnoreCase) ||
                                  (!string.IsNullOrWhiteSpace(speaker.Code) && string.Equals(k.Character, speaker.Code, StringComparison.OrdinalIgnoreCase)))));

                            if (ruleForSpeaker is null)
                            {
                                found.Add(new(KnowledgeBeforeReveal, sev,
                                    $"{where}: nhân vật \"{speaker.Name}\" chưa từng được biết về \"{entry.Name}\" theo Series Bible.",
                                    text, line.SceneId, line.Id, entryId, null));
                            }
                            else if (chapterNumber < ruleForSpeaker.FromChapter)
                            {
                                found.Add(new(KnowledgeBeforeReveal, sev,
                                    $"{where}: nhân vật \"{speaker.Name}\" chỉ được biết về \"{entry.Name}\" từ chương {ruleForSpeaker.FromChapter}, chưa biết ở chương {chapterNumber}.",
                                    text, line.SceneId, line.Id, entryId, null));
                            }
                        }
                        else if (entry.RevealedFromChapter is { } revFrom && chapterNumber < revFrom)
                        {
                            found.Add(new(KnowledgeBeforeReveal, sev,
                                $"{where}: nhân vật \"{speaker.Name}\" nhắc tới \"{entry.Name}\" trước khi được tiết lộ (từ chương {revFrom}).",
                                text, line.SceneId, line.Id, entryId, null));
                        }
                    }
                }
            }

            // READING_ORDER_WRONG: kiểm tra thứ tự panel gắn với dòng thoại so với thứ tự kịch bản
            if (line.PanelId.HasValue && (line.PanelReadingOrder.HasValue || line.PageNumber.HasValue))
            {
                if (prevPanelLine is not null && prevPanelLine.PanelId != line.PanelId)
                {
                    var prevPage = prevPanelLine.PageNumber ?? 1;
                    var currPage = line.PageNumber ?? 1;
                    var prevOrder = prevPanelLine.PanelReadingOrder ?? 0;
                    var currOrder = line.PanelReadingOrder ?? 0;

                    if (currPage < prevPage || (currPage == prevPage && currOrder < prevOrder))
                    {
                        found.Add(new(ReadingOrderWrong, ConsistencyFindingSeverity.Warning,
                            $"{where}: thứ tự đọc của panel (Trang {currPage}, thứ tự {currOrder}) đứng trước panel của dòng kịch bản trước (Trang {prevPage}, thứ tự {prevOrder}).",
                            text, line.SceneId, line.Id, null, null));
                    }
                }
                prevPanelLine = line;
            }

            foreach (var rule in rules)
                if (CheckRuleOnLine(rule, line, where, text, entries, chapterNumber) is { } hit) found.Add(hit);
        }
        return found;
    }

    /// <summary>
    /// Luật Producer cấu hình: có <c>Pattern</c> (regex, không phân biệt hoa thường) = chữ bị cấm, khớp trong thoại hoặc
    /// chỉ dẫn là vi phạm; RuleType <c>DialogueOverflow</c> với ConfigurationJson <c>{"maxChars": N}</c> = thoại dài quá N ký tự;
    /// hoặc các luật có cấu trúc CHARACTER_AFTER_DEATH, PROP_NOT_BEFORE, KNOWLEDGE_BEFORE_REVEAL, READING_ORDER_WRONG.
    /// </summary>
    private static DetectedFinding? CheckRuleOnLine(
        CheckRule rule, CheckLine line, string where, string text,
        IReadOnlyDictionary<Guid, CheckEntry>? entries = null, decimal chapterNumber = 1)
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

        if (string.Equals(rule.RuleType, CharacterAfterDeath, StringComparison.OrdinalIgnoreCase)
            || string.Equals(rule.RuleType, "CharacterDeath", StringComparison.OrdinalIgnoreCase))
        {
            if (ParseDeathRule(rule.ConfigurationJson) is { } death
                && line.SpeakerId == death.CharacterId
                && chapterNumber > death.DiesAtChapter
                && (death.RevivesAtChapter == null || chapterNumber < death.RevivesAtChapter))
            {
                var name = entries != null && line.SpeakerId.HasValue && entries.TryGetValue(line.SpeakerId.Value, out var ce)
                    ? ce.Name : "Nhân vật";
                return new(CharacterAfterDeath, severity,
                    $"{where}: vi phạm luật \"{rule.Name}\" - \"{name}\" đã chết từ chương {death.DiesAtChapter}.",
                    text, line.SceneId, line.Id, line.SpeakerId, rule.Id);
            }
        }

        if (string.Equals(rule.RuleType, PropNotBefore, StringComparison.OrdinalIgnoreCase)
            || string.Equals(rule.RuleType, "PropAvailability", StringComparison.OrdinalIgnoreCase))
        {
            if (ParsePropRule(rule.ConfigurationJson) is { } prop
                && line.LinkedEntryIds.Contains(prop.EntryId)
                && chapterNumber < prop.NotBeforeChapter)
            {
                var name = entries != null && entries.TryGetValue(prop.EntryId, out var ce) ? ce.Name : "Đạo cụ";
                return new(PropNotBefore, severity,
                    $"{where}: vi phạm luật \"{rule.Name}\" - \"{name}\" không được xuất hiện trước chương {prop.NotBeforeChapter}.",
                    text, line.SceneId, line.Id, prop.EntryId, rule.Id);
            }
        }

        return null;
    }

    private static (Guid CharacterId, int DiesAtChapter, int? RevivesAtChapter)? ParseDeathRule(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object
                && (root.TryGetProperty("characterId", out var c) || root.TryGetProperty("CharacterId", out c))
                && c.TryGetGuid(out var charId)
                && (root.TryGetProperty("diesAtChapter", out var d) || root.TryGetProperty("deathChapter", out d))
                && d.TryGetInt32(out var diesAt))
            {
                int? revives = (root.TryGetProperty("revivesAtChapter", out var r) || root.TryGetProperty("revivalChapter", out r)) && r.TryGetInt32(out var rv) ? rv : null;
                return (charId, diesAt, revives);
            }
        }
        catch (JsonException) { }
        return null;
    }

    private static (Guid EntryId, int NotBeforeChapter)? ParsePropRule(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object
                && (root.TryGetProperty("entryId", out var e) || root.TryGetProperty("EntryId", out e))
                && e.TryGetGuid(out var entryId)
                && (root.TryGetProperty("notBeforeChapter", out var nb) || root.TryGetProperty("availableFromChapter", out nb))
                && nb.TryGetInt32(out var ch))
            {
                return (entryId, ch);
            }
        }
        catch (JsonException) { }
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
