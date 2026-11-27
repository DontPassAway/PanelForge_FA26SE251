using System.Text.Json;
using PanelForge.Application.Features.Bible.Common;
using PanelForge.Domain.Entities.Bible;

namespace PanelForge.Application.Features.Bible.Models;

internal sealed record BibleEntrySnapshot(
    string Description,
    string DetailsJson,
    string? ReferenceImageUrl,
    string? Age = null,
    string? ArtistNotes = null)
{
    public static BibleEntrySnapshot From(BibleEntry entry, BibleEntryRevision? revision)
    {
        var description = entry.Description;
        var detailsJson = entry.DetailsJson;
        var referenceImageUrl = entry.ReferenceImageUrl;

        if (revision is not null)
        {
            try
            {
                using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(revision.SnapshotJson) ? "{}" : revision.SnapshotJson);
                var root = doc.RootElement;
                description = ReadString(root, "Description", description) ?? description;
                detailsJson = ReadString(root, "DetailsJson", detailsJson) ?? detailsJson;
                referenceImageUrl = ReadString(root, "ReferenceImageUrl", referenceImageUrl);
            }
            catch (JsonException)
            {
                // Dữ liệu snapshot hỏng dùng cache của entry làm fallback.
            }

            referenceImageUrl = revision.ConceptImageUrl ?? referenceImageUrl;
        }

        var parsed = BibleDetailsParser.Parse(detailsJson);
        return new(description, detailsJson, referenceImageUrl, parsed.Age, parsed.ArtistNotes);
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
