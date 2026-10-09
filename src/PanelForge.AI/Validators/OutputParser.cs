using System.Text.Json;
using PanelForge.AI.Contracts;

namespace PanelForge.AI.Validators;

/// <summary>Bóc JSON từ text của model (chịu được ```json fence / văn bản thừa) rồi parse. Không tin cấu trúc: lỗi → thông điệp cho lần sửa.</summary>
public static class OutputParser
{
    public static T? Parse<T>(string? text, out string? error) where T : class
    {
        error = null;
        var json = ExtractObject(text);
        if (json is null) { error = "Không tìm thấy đối tượng JSON trong output."; return null; }
        try
        {
            var value = JsonSerializer.Deserialize<T>(json, AiJson.Options);
            if (value is null) error = "JSON rỗng.";
            return value;
        }
        catch (JsonException ex)
        {
            error = $"JSON không hợp lệ: {ex.Message}";
            return null;
        }
    }

    /// <summary>true nếu có property tên <paramref name="name"/> ở bất kỳ cấp nào (dùng chặn trường lettering do Storyboard tự tạo).</summary>
    public static bool HasProperty(string? text, string name)
    {
        var json = ExtractObject(text);
        if (json is null) return false;
        try { using var doc = JsonDocument.Parse(json); return Walk(doc.RootElement, name); }
        catch (JsonException) { return false; }
    }

    private static bool Walk(JsonElement e, string name) => e.ValueKind switch
    {
        JsonValueKind.Object => e.EnumerateObject().Any(p =>
            string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase) || Walk(p.Value, name)),
        JsonValueKind.Array => e.EnumerateArray().Any(x => Walk(x, name)),
        _ => false
    };

    internal static string? ExtractObject(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        return start >= 0 && end > start ? text[start..(end + 1)] : null;
    }
}