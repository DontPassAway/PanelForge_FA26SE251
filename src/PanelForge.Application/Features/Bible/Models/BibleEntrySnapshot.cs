using System.Text.Json;
using PanelForge.Domain.Entities.Bible;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Bible.Models;

internal sealed record BibleEntrySnapshot(
    string Description,
    string? Subtitle,
    string DetailsJson,
    string? ReferenceImageUrl,
    BiblePriority Priority,
    bool StrictCheck)
{
    public static BibleEntrySnapshot From(BibleEntry entry, BibleEntryRevision? revision)
    {
        var description = entry.Description;
        var subtitle = entry.Subtitle;
        var detailsJson = entry.DetailsJson;
        var referenceImageUrl = entry.ReferenceImageUrl;
        var priority = entry.Priority;
        var strictCheck = entry.StrictCheck;

        if (revision is not null)
        {
            try
            {
                using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(revision.SnapshotJson) ? "{}" : revision.SnapshotJson);
                var root = doc.RootElement;
                description = ReadString(root, "Description", description) ?? description;
                subtitle = ReadString(root, "Subtitle", subtitle);
                detailsJson = ReadString(root, "DetailsJson", detailsJson) ?? detailsJson;
                referenceImageUrl = ReadString(root, "ReferenceImageUrl", referenceImageUrl);

                if (root.TryGetProperty("Priority", out var priorityElement)
                    && priorityElement.ValueKind == JsonValueKind.Number
                    && priorityElement.TryGetInt32(out var priorityValue))
                    priority = (BiblePriority)priorityValue;

                if (root.TryGetProperty("StrictCheck", out var strictElement)
                    && strictElement.ValueKind is JsonValueKind.True or JsonValueKind.False)
                    strictCheck = strictElement.GetBoolean();
            }
            catch (JsonException)
            {
                // Dữ liệu snapshot hỏng dùng cache của entry làm fallback.
            }

            referenceImageUrl = revision.ConceptImageUrl ?? referenceImageUrl;
        }

        return new(description, subtitle, detailsJson, referenceImageUrl, priority, strictCheck);
    }

    private static string? ReadString(JsonElement root, string name, string? fallback)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(name, out var value))
            return fallback;

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Null => null,
            _ => fallback
        };
    }
}
