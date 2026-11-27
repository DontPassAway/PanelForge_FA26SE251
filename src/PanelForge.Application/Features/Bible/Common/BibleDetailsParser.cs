using System.Text.Json;

namespace PanelForge.Application.Features.Bible.Common;

/// <summary>
/// Đại diện cho đặc tả tinh gọn của một mục Series Bible theo biến thể.
/// </summary>
public sealed record BibleDetails(string? Age, string? ArtistNotes)
{
    public bool IsEmpty => string.IsNullOrWhiteSpace(Age) && string.IsNullOrWhiteSpace(ArtistNotes);
}

/// <summary>
/// Xử lý phân tích và chuyển đổi tương thích ngược cho trường DetailsJson.
/// Tự động trích xuất 'age' và gom các trường thuộc tính cũ (4 nhóm body/face/costume/art hoặc flat attributes)
/// thành chuỗi ghi chú có cấu trúc trong 'artistNotes'.
/// </summary>
public static class BibleDetailsParser
{
    private static readonly HashSet<string> IgnoredKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "charturnaround",
        "charexpressions",
        "turnaroundgallery",
        "deliverables",
        "gallery"
    };

    private static readonly Dictionary<string, string> FieldLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["height"] = "Chiều cao",
        ["build"] = "Vóc dáng / Thể chất",
        ["hair"] = "Kiểu tóc / Màu tóc",
        ["eyes"] = "Màu mắt / Ánh mắt",
        ["marks"] = "Vết sẹo / Dấu vết đặc trưng",
        ["appearance"] = "Đặc điểm ngoại hình",
        ["outfit"] = "Trang phục",
        ["accessories"] = "Phụ kiện đi kèm",
        ["inkTone"] = "Tông mực / Đổ bóng",
        ["expressions"] = "Biểu cảm đặc trưng",
        ["forbidden"] = "Lưu ý cấm kỵ / Tránh vẽ sai",
        ["environmentType"] = "Loại bối cảnh / Môi trường",
        ["scale"] = "Quy mô / Diện tích",
        ["atmosphere"] = "Không khí / Bầu không khí",
        ["architecture"] = "Kiến trúc đặc trưng",
        ["layout"] = "Bố cục không gian",
        ["cameraAngles"] = "Góc máy gợi ý",
        ["propType"] = "Loại đạo cụ",
        ["sizeWeight"] = "Kích thước & trọng lượng",
        ["material"] = "Chất liệu",
        ["owner"] = "Người sở hữu / Thường mang theo",
        ["stateByChapter"] = "Trạng thái theo chương",
        ["termType"] = "Loại thuật ngữ",
        ["origin"] = "Nguồn gốc xuất xứ",
        ["usage"] = "Cách sử dụng / Ngữ cảnh",
        ["colorPalette"] = "Bảng màu",
        ["lineArtRules"] = "Quy tắc nét vẽ"
    };

    /// <summary>
    /// Đọc an toàn chuỗi DetailsJson (hỗ trợ cả định dạng mới và cũ).
    /// </summary>
    public static BibleDetails Parse(string? detailsJson)
    {
        if (string.IsNullOrWhiteSpace(detailsJson) || detailsJson.Trim() == "{}")
            return new BibleDetails(null, null);

        try
        {
            using var doc = JsonDocument.Parse(detailsJson);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return new BibleDetails(null, detailsJson.Trim());
            }

            string? age = null;
            string? directArtistNotes = null;
            var bulletLines = new List<string>();

            // 1. Kiểm tra age trực tiếp ở root
            if (TryGetStringProperty(root, "age", out var directAge) ||
                TryGetStringProperty(root, "Age", out directAge))
            {
                age = directAge;
            }

            // 2. Kiểm tra artistNotes trực tiếp ở root
            if (TryGetStringProperty(root, "artistNotes", out var notes) ||
                TryGetStringProperty(root, "ArtistNotes", out notes))
            {
                directArtistNotes = notes;
            }

            // 3. Nếu có attributes bọc ngoài (format FE cũ: { attributes: { ... } })
            if (root.TryGetProperty("attributes", out var attrsElem) ||
                root.TryGetProperty("Attributes", out attrsElem))
            {
                if (attrsElem.ValueKind == JsonValueKind.Object)
                {
                    if (string.IsNullOrWhiteSpace(directArtistNotes) &&
                        (TryGetStringProperty(attrsElem, "artistNotes", out var nestedNotes) ||
                         TryGetStringProperty(attrsElem, "ArtistNotes", out nestedNotes)))
                    {
                        directArtistNotes = nestedNotes;
                    }
                    ExtractFromObject(attrsElem, ref age, bulletLines);
                }
            }

            // 4. Nếu có các nhóm cũ (body, face, costume, art, space, fixedDetails, prop, terminology, style)
            foreach (var prop in root.EnumerateObject())
            {
                var name = prop.Name;
                if (name.Equals("age", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("artistNotes", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("attributes", StringComparison.OrdinalIgnoreCase) ||
                    IgnoredKeys.Contains(name))
                {
                    continue;
                }

                if (prop.Value.ValueKind == JsonValueKind.Object)
                {
                    ExtractFromObject(prop.Value, ref age, bulletLines);
                }
                else if (prop.Value.ValueKind == JsonValueKind.String)
                {
                    var val = prop.Value.GetString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(val))
                    {
                        var label = FieldLabels.TryGetValue(name, out var friendly) ? friendly : name;
                        bulletLines.Add($"- {label}: {val}");
                    }
                }
            }

            // Tổng hợp artistNotes
            var uniqueBulletLines = bulletLines.Distinct(StringComparer.Ordinal).ToArray();
            string? finalArtistNotes;
            if (!string.IsNullOrWhiteSpace(directArtistNotes))
            {
                if (uniqueBulletLines.Length > 0)
                {
                    finalArtistNotes = $"{directArtistNotes.Trim()}\n\n{string.Join("\n", uniqueBulletLines)}";
                }
                else
                {
                    finalArtistNotes = directArtistNotes.Trim();
                }
            }
            else
            {
                finalArtistNotes = uniqueBulletLines.Length > 0 ? string.Join("\n", uniqueBulletLines) : null;
            }

            return new BibleDetails(
                string.IsNullOrWhiteSpace(age) ? null : age.Trim(),
                string.IsNullOrWhiteSpace(finalArtistNotes) ? null : finalArtistNotes.Trim()
            );
        }
        catch (JsonException)
        {
            return new BibleDetails(null, detailsJson.Trim());
        }
    }

    private static void ExtractFromObject(JsonElement element, ref string? age, List<string> bulletLines)
    {
        foreach (var prop in element.EnumerateObject())
        {
            var key = prop.Name;
            if (IgnoredKeys.Contains(key)) continue;

            if (key.Equals("age", StringComparison.OrdinalIgnoreCase))
            {
                if (age == null && prop.Value.ValueKind == JsonValueKind.String)
                {
                    var a = prop.Value.GetString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(a)) age = a;
                }
                continue;
            }

            // artistNotes is the canonical unified brief, not a legacy labelled bullet.
            // Parse reads it before this compatibility projection runs.
            if (key.Equals("artistNotes", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("artist_notes", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (prop.Value.ValueKind == JsonValueKind.String)
            {
                var str = prop.Value.GetString()?.Trim();
                if (!string.IsNullOrWhiteSpace(str))
                {
                    var label = FieldLabels.TryGetValue(key, out var friendly) ? friendly : key;
                    bulletLines.Add($"- {label}: {str}");
                }
            }
        }
    }

    private static bool TryGetStringProperty(JsonElement element, string propName, out string? value)
    {
        value = null;
        if (element.TryGetProperty(propName, out var p) && p.ValueKind == JsonValueKind.String)
        {
            value = p.GetString()?.Trim();
            return !string.IsNullOrWhiteSpace(value);
        }
        return false;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    /// Đóng gói gọn thành JSON chuẩn: {"age": "...", "artistNotes": "..."}.
    /// </summary>
    public static string Serialize(string? age, string? artistNotes)
    {
        var cleanAge = string.IsNullOrWhiteSpace(age) ? null : age.Trim();
        var cleanNotes = string.IsNullOrWhiteSpace(artistNotes) ? null : artistNotes.Trim();

        if (cleanAge == null && cleanNotes == null)
            return "{}";

        var dict = new Dictionary<string, string>();
        if (cleanAge != null) dict["age"] = cleanAge;
        if (cleanNotes != null) dict["artistNotes"] = cleanNotes;

        return JsonSerializer.Serialize(dict, JsonOptions);
    }

    /// <summary>
    /// Chuẩn hóa bất kỳ chuỗi DetailsJson cũ nào thành định dạng gọn mới.
    /// </summary>
    public static string NormalizeJson(string? rawJson)
    {
        var details = Parse(rawJson);
        return Serialize(details.Age, details.ArtistNotes);
    }
}
