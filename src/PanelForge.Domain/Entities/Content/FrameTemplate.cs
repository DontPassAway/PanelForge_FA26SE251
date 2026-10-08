using System.Text.Json;
using PanelForge.Domain.Common;
using PanelForge.Domain.Enums;

namespace PanelForge.Domain.Entities.Content;

/// <summary>
/// Frame template (khung trang dựng sẵn) — chỉ là cấu hình khởi đầu (Core Flow CF2 bước 9, phiếu V1.2):
/// áp dụng vào Page thì SAO CHÉP hình học thành các Panel độc lập; sửa/xoá template sau đó không đổi Page đã có.
/// WorkspaceId = null: template có sẵn của hệ thống (<see cref="BuiltIns"/>, không lưu DB).
/// Hình học chuẩn hoá 0–1 theo khổ trang nên dùng được cho mọi kích thước canvas.
/// </summary>
public class FrameTemplate : BaseEntity
{
    public const int MaxSlots = 20;

    public Guid? WorkspaceId { get; private set; }
    public string Name { get; private set; } = default!;
    public LayoutFormat LayoutFormat { get; private set; }

    /// <summary>JSON mảng <see cref="FrameSlot"/>.</summary>
    public string SlotsJson { get; private set; } = "[]";

    private FrameTemplate() { }

    public IReadOnlyList<FrameSlot> Slots =>
        JsonSerializer.Deserialize<List<FrameSlot>>(SlotsJson, JsonOpts) ?? [];

    public static FrameTemplate Create(Guid workspaceId, string name, LayoutFormat layoutFormat, IReadOnlyList<FrameSlot> slots)
    {
        var t = Build(name, layoutFormat, slots);
        t.WorkspaceId = workspaceId;
        return t;
    }

    private static FrameTemplate Build(string name, LayoutFormat layoutFormat, IReadOnlyList<FrameSlot> slots, Guid? id = null)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 100)
            throw new ArgumentException("Tên template bắt buộc, tối đa 100 ký tự.", nameof(name));
        ValidateSlots(slots);
        var t = new FrameTemplate
        {
            Name = name.Trim(),
            LayoutFormat = layoutFormat,
            SlotsJson = JsonSerializer.Serialize(slots, JsonOpts)
        };
        if (id is { } fixedId) t.Id = fixedId;
        return t;
    }

    public static void ValidateSlots(IReadOnlyList<FrameSlot> slots)
    {
        if (slots is null || slots.Count is < 1 or > MaxSlots)
            throw new ArgumentException($"Template phải có từ 1 đến {MaxSlots} khung.");
        const double eps = 1e-6;
        foreach (var s in slots)
        {
            if (s.W <= 0 || s.H <= 0 || s.X < 0 || s.Y < 0 || s.X + s.W > 1 + eps || s.Y + s.H > 1 + eps)
                throw new ArgumentException("Toạ độ khung phải chuẩn hoá trong [0, 1] và nằm trong trang (w, h > 0).");
            if (s.Corners is { } c && (c.Count != 4 || c.Any(p => p.X is < 0 or > 1 || p.Y is < 0 or > 1)))
                throw new ArgumentException("Corners phải gồm đúng 4 điểm (tl, tr, br, bl) chuẩn hoá 0–1 trong hộp của khung.");
        }
    }

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    // ── Template có sẵn (tự soạn, không sao chép từ dự án bên ngoài) ─────────────────
    private const double M = 0.05;   // lề trang
    private const double G = 0.02;   // khoảng cách giữa khung (gutter)

    /// <summary>Lưới theo hàng: rows[i] = (tỉ lệ chiều cao, số cột).</summary>
    private static List<FrameSlot> Rows(params (double Weight, int Cols)[] rows)
    {
        var slots = new List<FrameSlot>();
        var usableH = 1 - 2 * M - G * (rows.Length - 1);
        var total = rows.Sum(r => r.Weight);
        var y = M;
        foreach (var (weight, cols) in rows)
        {
            var h = usableH * weight / total;
            var w = (1 - 2 * M - G * (cols - 1)) / cols;
            for (var c = 0; c < cols; c++)
                slots.Add(new FrameSlot(Math.Round(M + c * (w + G), 4), Math.Round(y, 4), Math.Round(w, 4), Math.Round(h, 4)));
            y += h + G;
        }
        return slots;
    }

    private static FrameTemplate BuiltIn(string id, string name, List<FrameSlot> slots, LayoutFormat format = LayoutFormat.StandardPage)
        => Build(name, format, slots, Guid.Parse(id));

    public static readonly IReadOnlyList<FrameTemplate> BuiltIns =
    [
        BuiltIn("f0000000-0000-0000-0000-000000000001", "Splash (1 khung)", Rows((1, 1))),
        BuiltIn("f0000000-0000-0000-0000-000000000002", "Splash tràn lề (không viền)",
            [new FrameSlot(0, 0, 1, 1, Borderless: true)]),
        BuiltIn("f0000000-0000-0000-0000-000000000003", "2 tầng ngang", Rows((1, 1), (1, 1))),
        BuiltIn("f0000000-0000-0000-0000-000000000004", "3 tầng ngang", Rows((1, 1), (1, 1), (1, 1))),
        BuiltIn("f0000000-0000-0000-0000-000000000005", "4 khung (lưới 2×2)", Rows((1, 2), (1, 2))),
        BuiltIn("f0000000-0000-0000-0000-000000000006", "6 khung (lưới 3 hàng × 2)", Rows((1, 2), (1, 2), (1, 2))),
        BuiltIn("f0000000-0000-0000-0000-000000000007", "1 lớn + 2 nhỏ", Rows((2, 1), (1, 2))),
        BuiltIn("f0000000-0000-0000-0000-000000000008", "5 khung (1 + 2 + 2)", Rows((1, 1), (1, 2), (1, 2))),
        BuiltIn("f0000000-0000-0000-0000-000000000009", "6 khung hành động (1 + 2 + 1 + 2)", Rows((1, 1), (1, 2), (1.3, 1), (1, 2))),
        BuiltIn("f0000000-0000-0000-0000-00000000000a", "Khung nghiêng (2 khung chéo)",
        [
            new FrameSlot(M, M, 1 - 2 * M, 0.45, Corners: [new(0, 0), new(1, 0), new(1, 0.8), new(0, 1)]),
            new FrameSlot(M, 0.42, 1 - 2 * M, 0.53, Corners: [new(0, 0.12), new(1, 0), new(1, 1), new(0, 1)]),
        ]),
        BuiltIn("f0000000-0000-0000-0000-00000000000b", "Trang đôi: 1 khung toàn cảnh + 3 khung",
            Rows((2, 1), (1, 3)), LayoutFormat.SpreadDouble),
    ];
}

/// <summary>
/// Một khung trong template. X, Y, W, H chuẩn hoá 0–1 theo trang; Corners (tuỳ chọn) là 4 đỉnh tl, tr, br, bl chuẩn hoá
/// 0–1 trong hộp của khung, cho khung nghiêng; Borderless = khung không viền (thuộc tính hiển thị).
/// Thứ tự trong mảng KHÔNG phải thứ tự đọc: thứ tự đọc tính lại theo ReadingDirection của Series khi áp dụng.
/// </summary>
public sealed record FrameSlot(
    double X, double Y, double W, double H,
    IReadOnlyList<FramePoint>? Corners = null,
    bool Borderless = false);

public sealed record FramePoint(double X, double Y);
