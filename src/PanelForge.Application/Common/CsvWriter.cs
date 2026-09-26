using System.Globalization;
using System.Text;

namespace PanelForge.Application.Common;

/// <param name="Truncated">true nếu kết quả bị cắt ở giới hạn số dòng export.</param>
public sealed record CsvExport(string FileName, byte[] Content, int RowCount, bool Truncated);

/// <summary>
/// Sinh CSV (RFC 4180) cho các chức năng export (UC-15 bước 4).
/// Ô bắt đầu bằng = + - @ được thêm dấu ' để chặn CSV/formula injection khi mở bằng Excel.
/// </summary>
public static class CsvWriter
{
    public static byte[] Write(IEnumerable<string> headers, IEnumerable<IEnumerable<object?>> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(',', headers.Select(Escape)));
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(',', row.Select(v => Escape(Format(v)))));
        }

        // BOM UTF-8 để Excel hiển thị đúng tiếng Việt
        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
    }

    private static string Format(object? value) => value switch
    {
        null => string.Empty,
        DateTime dt => dt.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty
    };

    private static string Escape(string value)
    {
        if (value.Length > 0 && "=+-@\t\r".Contains(value[0]))
            value = "'" + value;

        return value.IndexOfAny([',', '"', '\n', '\r']) >= 0
            ? "\"" + value.Replace("\"", "\"\"") + "\""
            : value;
    }
}
