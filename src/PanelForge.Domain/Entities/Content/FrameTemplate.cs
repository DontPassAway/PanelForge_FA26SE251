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

    /// <summary>Phân loại theo mục đích kể chuyện: narrative, action, cinematic, webtoon.</summary>
    public string Category { get; private set; } = "narrative";

    /// <summary>Mã chuẩn hoá của template (ví dụ TPL-MANGA-01, TPL-COMIC-01).</summary>
    public string? Code { get; private set; }

    /// <summary>Mô tả công năng và nhịp điệu của bố cục.</summary>
    public string? Description { get; private set; }

    /// <summary>JSON mảng <see cref="FrameSlot"/>.</summary>
    public string SlotsJson { get; private set; } = "[]";

    private FrameTemplate() { }

    public IReadOnlyList<FrameSlot> Slots =>
        JsonSerializer.Deserialize<List<FrameSlot>>(SlotsJson, JsonOpts) ?? [];

    public static FrameTemplate Create(Guid workspaceId, string name, LayoutFormat layoutFormat, IReadOnlyList<FrameSlot> slots, string category = "narrative", string? code = null, string? description = null)
    {
        var t = Build(name, layoutFormat, slots, category: category, code: code, description: description);
        t.WorkspaceId = workspaceId;
        return t;
    }

    private static FrameTemplate Build(
        string name,
        LayoutFormat layoutFormat,
        IReadOnlyList<FrameSlot> slots,
        Guid? id = null,
        string category = "narrative",
        string? code = null,
        string? description = null)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 100)
            throw new ArgumentException("Tên template bắt buộc, tối đa 100 ký tự.", nameof(name));
        ValidateSlots(slots);
        var t = new FrameTemplate
        {
            Name = name.Trim(),
            LayoutFormat = layoutFormat,
            SlotsJson = JsonSerializer.Serialize(slots, JsonOpts),
            Category = string.IsNullOrWhiteSpace(category) ? "narrative" : category.Trim().ToLowerInvariant(),
            Code = code?.Trim(),
            Description = description?.Trim()
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

    // ── Template có sẵn (tự soạn, chuẩn in ấn Manga hiện đại lề 2.5% tối ưu không gian) ─────────────────
    private const double Mx = 0.025;  // lề ngang 2.5% (khung chiếm 95% bề rộng trang)
    private const double My = 0.025;  // lề dọc 2.5% (khung chiếm 95% bề cao trang)
    private const double Gy = 0.015;  // khoảng cách giữa các hàng (gutter dọc) 1.5%
    private const double Gx = 0.012;  // khoảng cách giữa các cột (gutter ngang) 1.2%

    /// <summary>Lưới theo hàng: rows[i] = (tỉ lệ chiều cao, số cột).</summary>
    private static List<FrameSlot> Rows(params (double Weight, int Cols)[] rows)
    {
        var slots = new List<FrameSlot>();
        var usableH = 1 - 2 * My - Gy * (rows.Length - 1);
        var total = rows.Sum(r => r.Weight);
        var y = My;
        foreach (var (weight, cols) in rows)
        {
            var h = usableH * weight / total;
            var w = (1 - 2 * Mx - Gx * (cols - 1)) / cols;
            for (var c = 0; c < cols; c++)
                slots.Add(new FrameSlot(Math.Round(Mx + c * (w + Gx), 4), Math.Round(y, 4), Math.Round(w, 4), Math.Round(h, 4)));
            y += h + Gy;
        }
        return slots;
    }

    private static FrameTemplate BuiltIn(
        string id,
        string name,
        List<FrameSlot> slots,
        LayoutFormat format = LayoutFormat.StandardPage,
        string category = "narrative",
        string? code = null,
        string? description = null)
        => Build(name, format, slots, Guid.Parse(id), category, code, description);

    public static readonly IReadOnlyList<FrameTemplate> BuiltIns =
    [
        // ── NHÓM 1: TỰ SỰ TIÊU CHUẨN (STANDARD & NARRATIVE) ─────────────────────────
        BuiltIn("f0000000-0000-0000-0000-00000000000c", "4-Koma Classic (Ki-shō-ten-ketsu)",
            [
                new FrameSlot(Mx, My, 1 - 2 * Mx, 0.2285),
                new FrameSlot(Mx, Math.Round(My + 0.2285 + 0.012, 4), 1 - 2 * Mx, 0.2285),
                new FrameSlot(Mx, Math.Round(My + 2 * (0.2285 + 0.012), 4), 1 - 2 * Mx, 0.2285),
                new FrameSlot(Mx, Math.Round(My + 3 * (0.2285 + 0.012), 4), 1 - 2 * Mx, 0.2285),
            ],
            category: "narrative", code: "TPL-MANGA-01",
            description: "4 khung truyện thẳng hàng (Khởi - Thừa - Chuyển - Hợp) chuẩn hài hước & đời thường"),

        BuiltIn("f0000000-0000-0000-0000-000000000008", "3-Tier Balanced (5 Khung RTL)",
            [
                // Tier 1: 2 khung (phải thiết lập cảnh, trái phản ứng)
                new FrameSlot(0.506, My, 0.469, 0.27),
                new FrameSlot(Mx, My, 0.469, 0.27),
                // Tier 2: 1 khung lớn bao ngang hội thoại
                new FrameSlot(Mx, 0.31, 1 - 2 * Mx, 0.38),
                // Tier 3: 2 khung chuyển trang
                new FrameSlot(0.506, 0.705, 0.469, 0.27),
                new FrameSlot(Mx, 0.705, 0.469, 0.27),
            ],
            category: "narrative", code: "TPL-MANGA-02",
            description: "Bố cục 3 tầng cổ điển: Tier 1 mở cảnh, Tier 2 trọng tâm kịch tính, Tier 3 chuyển tiếp trang"),

        BuiltIn("f0000000-0000-0000-0000-000000000006", "3-Tier Dense (6 Khung Lưới 3×2)",
            Rows((1, 2), (1, 2), (1, 2)),
            category: "narrative", code: "TPL-MANGA-03",
            description: "Lưới 3 hàng × 2 cột cho các phân cảnh nhiều đối thoại, điều tra, tranh luận gay cấn"),

        BuiltIn("f0000000-0000-0000-0000-00000000000e", "Classic 9-Panel Grid (Watchmen Style)",
            Rows((1, 3), (1, 3), (1, 3)),
            category: "narrative", code: "TPL-COMIC-01",
            description: "Lưới 3×3 phong cách Watchmen/DC Classic: nhịp kể đều đặn, phân tích tâm lý sâu sắc"),

        BuiltIn("f0000000-0000-0000-0000-000000000003", "2 Tầng Ngang Cơ Bản",
            Rows((1, 1), (1, 1)),
            category: "narrative",
            description: "Chia đôi trang theo chiều ngang, cân bằng nhịp kể"),

        BuiltIn("f0000000-0000-0000-0000-000000000004", "3 Tầng Ngang Cơ Bản",
            Rows((1, 1), (1, 1), (1, 1)),
            category: "narrative",
            description: "3 dải ngang bằng nhau cho phân cảnh diễn biến theo trình tự thời gian"),

        BuiltIn("f0000000-0000-0000-0000-000000000005", "4 Khung (Lưới 2×2)",
            Rows((1, 2), (1, 2)),
            category: "narrative",
            description: "4 ô vuông cân xứng cho nhịp kể phân đoạn"),

        // ── NHÓM 2: HÀNH ĐỘNG & KỊCH TÍNH (ACTION & HIGH-TENSION) ────────────────────
        BuiltIn("f0000000-0000-0000-0000-00000000000a", "Dynamic Shonen Slanted (4 Khung Cắt Chéo)",
            [
                new FrameSlot(Mx, My, 1 - 2 * Mx, 0.28, Corners: [new(0, 0), new(1, 0), new(1, 0.75), new(0, 1)]),
                new FrameSlot(0.44, 0.26, 0.535, 0.38, Corners: [new(0, 0.22), new(1, 0), new(1, 1), new(0, 0.85)]),
                new FrameSlot(Mx, 0.32, 0.40, 0.38, Corners: [new(0, 0), new(1, 0.15), new(1, 1), new(0, 0.85)]),
                new FrameSlot(Mx, 0.685, 1 - 2 * Mx, 0.29, Corners: [new(0, 0.15), new(1, 0.05), new(1, 1), new(0, 1)]),
            ],
            category: "action", code: "TPL-MANGA-04",
            description: "Rãnh cắt chéo góc 15°-25° tạo xung lực gia tốc cực mạnh cho phân cảnh combat, ra đòn chớp nhoáng"),

        BuiltIn("f0000000-0000-0000-0000-000000000010", "Inset Panels (Khung Lồng Khung)",
            [
                new FrameSlot(Mx, My, 1 - 2 * Mx, 1 - 2 * My),
                new FrameSlot(0.55, 0.08, 0.39, 0.26),
                new FrameSlot(0.06, 0.62, 0.39, 0.26),
            ],
            category: "action", code: "TPL-MANGA-06",
            description: "1 khung toàn cảnh chiến trường làm nền lồng 2 khung cận cảnh biểu cảm và chi tiết vũ khí"),

        BuiltIn("f0000000-0000-0000-0000-000000000009", "Shonen Action Dense (6 Khung)",
            Rows((1, 1), (1, 2), (1.3, 1), (1, 2)),
            category: "action",
            description: "1 mở đầu + 2 dồn dập + 1 cao trào lớn + 2 dư chấn phản ứng"),

        // ── NHÓM 3: ĐẠI CẢNH & ĐIỂM NHẤN (CINEMATIC & SPLASH) ─────────────────────────
        BuiltIn("f0000000-0000-0000-0000-000000000002", "Full Splash Bleed (1 Khung Tràn Lề)",
            [new FrameSlot(0, 0, 1, 1, Borderless: true)],
            category: "cinematic",
            description: "1 panel tràn viền 100% đến hết vùng Bleed dành cho trang bìa hoặc đại cảnh kết liễu"),

        BuiltIn("f0000000-0000-0000-0000-000000000001", "Splash 1 Khung (Có Viền)",
            Rows((1, 1)),
            category: "cinematic",
            description: "1 khung viền đơn toàn trang chuẩn lề in ấn"),

        BuiltIn("f0000000-0000-0000-0000-00000000000d", "Hero Splash 70/30 (Dominant Layout)",
            [
                new FrameSlot(Mx, My, 1 - 2 * Mx, 0.67),
                new FrameSlot(0.665, 0.71, 0.31, 0.265),
                new FrameSlot(0.345, 0.71, 0.308, 0.265),
                new FrameSlot(Mx, 0.71, 0.308, 0.265),
            ],
            category: "cinematic", code: "TPL-MANGA-05",
            description: "Khung chiếm 70% trang trên cho cảnh đại chiến bùng nổ, 3 khung hẹp 30% dưới cho phản hồi của đồng đội"),

        BuiltIn("f0000000-0000-0000-0000-00000000000f", "Cinematic Widescreen (3 Bars)",
            Rows((1, 1), (1, 1), (1.1, 1)),
            category: "cinematic", code: "TPL-COMIC-02",
            description: "3 thanh dài ngang tỉ lệ màn ảnh rộng cho cảnh thiên nhiên kéo dài hoặc du hành thời gian"),

        BuiltIn("f0000000-0000-0000-0000-000000000007", "1 Lớn + 2 Nhỏ (Phản Ứng Đối Xứng)",
            Rows((2, 1), (1, 2)),
            category: "cinematic",
            description: "1 khung chính phía trên kết hợp 2 khung cận cảnh phân tích bên dưới"),

        BuiltIn("f0000000-0000-0000-0000-00000000000b", "Trang Đôi: 1 Khung Toàn Cảnh + 3 Khung",
            Rows((2, 1), (1, 3)), LayoutFormat.SpreadDouble,
            category: "cinematic",
            description: "Bố cục trang đôi (Spread): Đại cảnh trải dài 2 trang kết hợp 3 khung nhịp điệu"),

        // ── NHÓM 4: WEBTOON / MOBILE VERTICAL SCROLL ──────────────────────────────────
        BuiltIn("f0000000-0000-0000-0000-000000000011", "Vertical Mobile Flow (3 Khung Cuộn)",
            [
                new FrameSlot(0.04, 0.04, 0.92, 0.27),
                new FrameSlot(0.04, 0.36, 0.92, 0.27),
                new FrameSlot(0.04, 0.68, 0.92, 0.28),
            ],
            LayoutFormat.WebtoonLongstrip,
            category: "webtoon", code: "TPL-WEBTOON-01",
            description: "Bố cục cuộn dọc tiêu chuẩn smartphone 9:16 với khoảng lặng ngừng nghỉ (negative space)"),

        BuiltIn("f0000000-0000-0000-0000-000000000012", "Webtoon Split Comparison (Song Song)",
            [
                new FrameSlot(0.04, 0.04, 0.92, 0.32),
                new FrameSlot(0.04, 0.40, 0.445, 0.55),
                new FrameSlot(0.515, 0.40, 0.445, 0.55),
            ],
            LayoutFormat.WebtoonLongstrip,
            category: "webtoon", code: "TPL-WEBTOON-02",
            description: "1 khung dẫn đề ở trên + 2 cột song song 50-50 để miêu tả 2 góc nhìn đối thoại đồng thời"),

        BuiltIn("f0000000-0000-0000-0000-000000000013", "Webtoon Borderless Floating Flow",
            [
                new FrameSlot(0, 0, 1, 0.45, Borderless: true),
                new FrameSlot(0.08, 0.52, 0.84, 0.42, Borderless: true),
            ],
            LayoutFormat.WebtoonLongstrip,
            category: "webtoon", code: "TPL-WEBTOON-03",
            description: "Khung tranh không viền chìm dần vào trang cuộn, tạo cảm xúc bay bổng hoặc hồi tưởng"),
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
