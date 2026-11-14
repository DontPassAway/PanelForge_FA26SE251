using System.Text.RegularExpressions;
using PanelForge.Domain.Common;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Chapters.Services;

public sealed record SpeakerCandidate(
    string DisplayName,
    string NormalizedName,
    IReadOnlyList<ScriptLine> Lines);

/// <summary>
/// Hàm thuần xử lý khớp người nói cho kịch bản (BIB-12, 3A, Q16):
/// Chuẩn hoá SpeakerLabel của dòng Dialogue và Thought (bỏ hậu tố (nghĩ) / (nghi) / (thought)),
/// dùng BibleNameNormalizer, và gom nhóm theo tên chuẩn hoá.
/// </summary>
public static class SpeakerResolver
{
    private static readonly Regex ThoughtSuffixRegex = new(
        @"\s*\((?:nghĩ|nghi|thought)\)\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Gỡ bỏ hậu tố (nghĩ), (nghi), (thought) khỏi nhãn người nói.
    /// </summary>
    public static string StripThoughtSuffix(string? speakerLabel)
    {
        if (string.IsNullOrWhiteSpace(speakerLabel))
            return string.Empty;

        var trimmed = speakerLabel.Trim();
        var stripped = ThoughtSuffixRegex.Replace(trimmed, string.Empty);
        return stripped.Trim();
    }

    /// <summary>
    /// Chuẩn hoá nhãn người nói: gỡ hậu tố suy nghĩ và chuẩn hoá Unicode/case.
    /// </summary>
    public static string NormalizeSpeaker(string? speakerLabel)
    {
        var clean = StripThoughtSuffix(speakerLabel);
        return BibleNameNormalizer.Normalize(clean);
    }

    /// <summary>
    /// Trích xuất danh sách các ứng viên người nói từ các dòng Dialogue và Thought trong chương.
    /// Không quét chữ trong mô tả/lời dẫn/SFX.
    /// </summary>
    public static IReadOnlyList<SpeakerCandidate> ExtractSpeakerCandidates(IEnumerable<ScriptLine> lines)
    {
        var dialogueOrThoughtLines = lines
            .Where(l => l.Kind is ScriptLineKind.Dialogue or ScriptLineKind.Thought)
            .ToList();

        var grouped = new Dictionary<string, (string DisplayName, List<ScriptLine> Lines)>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in dialogueOrThoughtLines)
        {
            var rawLabel = line.SpeakerLabel;
            if (string.IsNullOrWhiteSpace(rawLabel))
                continue;

            var cleanDisplay = StripThoughtSuffix(rawLabel);
            if (string.IsNullOrWhiteSpace(cleanDisplay))
                continue;

            var normKey = BibleNameNormalizer.Normalize(cleanDisplay);
            if (string.IsNullOrEmpty(normKey))
                continue;

            if (!grouped.TryGetValue(normKey, out var entry))
            {
                entry = (cleanDisplay, new List<ScriptLine>());
                grouped[normKey] = entry;
            }
            entry.Lines.Add(line);
        }

        return grouped.Select(kv => new SpeakerCandidate(kv.Value.DisplayName, kv.Key, kv.Value.Lines)).ToList();
    }
}
