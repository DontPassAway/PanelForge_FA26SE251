namespace PanelForge.Domain.Enums;

/// <summary>
/// Ngoại lệ có chủ ý trong tự sự (consistency-design §3.4).
/// Giúp hệ thống phân biệt sự kiện mạch chính với hồi tưởng, giấc mơ, lời nói dối để không báo lỗi sai (false positive).
/// </summary>
public enum NarrationMode
{
    /// <summary>Mạch chính / hiện tại trong truyện (áp dụng đầy đủ mọi quy tắc).</summary>
    Present = 1,

    /// <summary>Hồi tưởng quá khứ (nhân vật đã chết vẫn có thể xuất hiện/nói ở quá khứ).</summary>
    Flashback = 2,

    /// <summary>Giấc mơ (không tính là nhân vật biết thật; đạo cụ/người chết xuất hiện không tính là thật).</summary>
    Dream = 3,

    /// <summary>Tưởng tượng / suy tưởng (tương tự Dream, không tạo sự thật thế giới).</summary>
    Imagination = 4,

    /// <summary>Lời nói dối (là tuyên bố của nhân vật, không phải sự thật thế giới; không phạt mâu thuẫn kiến thức).</summary>
    Lie = 5,

    /// <summary>Tin đồn / lời đồn (tuyên bố chưa kiểm chứng).</summary>
    Rumor = 6,

    /// <summary>Người kể chuyện không đáng tin (hạ mức lỗi thành Warning/Info).</summary>
    UnreliableNarrator = 7
}
