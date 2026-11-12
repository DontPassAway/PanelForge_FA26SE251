using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PanelForge.Domain.Common;
using PanelForge.Domain.Entities.Bible;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Entities.Workspaces;
using PanelForge.Domain.Enums;

namespace PanelForge.Infrastructure.Persistence.Seeders;

/// <summary>
/// Dữ liệu mẫu cho demo AI Multi-Agent (CHỈ chạy khi bật AiService:SeedDemoData, môi trường Development).
/// Studio Moonlight / Series "Thành phố mưa" / Chapter 3 / một Scene. Bible ghi rõ An chỉ nhận chìa khóa bạc từ Chapter 5.
/// Idempotent: nếu Workspace mẫu (Id cố định) đã có thì không làm gì, không bao giờ sửa dữ liệu có sẵn.
/// Cần tài khoản dev của DevUserSeeder (producer, writer, editor, artist).
/// </summary>
public static class AiDemoSeeder
{
    public static readonly Guid WorkspaceId = Guid.Parse("a1000000-0000-0000-0000-000000000001");
    public static readonly Guid SeriesId = Guid.Parse("a1000000-0000-0000-0000-000000000002");
    public static readonly Guid ChapterId = Guid.Parse("a1000000-0000-0000-0000-000000000003");
    public static readonly Guid SceneId = Guid.Parse("a1000000-0000-0000-0000-000000000004");

    private static readonly Guid ProducerUser = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid WriterUser = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid ArtistUser = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid EditorUser = Guid.Parse("77777777-7777-7777-7777-777777777777");
    private static readonly Guid LettererUser = Guid.Parse("66666666-6666-6666-6666-666666666666");

    public const string AnDescription =
        "An (17 tuổi) là thợ học việc ở tiệm đồng hồ trên phố Mưa, có thói quen đếm tiếng mưa trên mái. " +
        "An chỉ nhận được chìa khóa bạc từ Chương 5; trước Chương 5, An chưa từng sở hữu hay nhìn thấy chiếc chìa khóa này.";

    /// <returns>true nếu vừa seed, false nếu đã có (bỏ qua) hoặc thiếu tài khoản dev.</returns>
    public static async Task<bool> SeedAsync(PanelForgeDbContext db, CancellationToken ct = default)
    {
        if (await db.StudioWorkspaces.IgnoreQueryFilters().AnyAsync(w => w.Id == WorkspaceId, ct))
            return false;

        var userIds = new[] { ProducerUser, WriterUser, ArtistUser, EditorUser };
        var existing = await db.Users.Where(u => userIds.Contains(u.Id)).Select(u => u.Id).ToListAsync(ct);
        if (existing.Count != userIds.Length)
            return false; // chưa có tài khoản dev (DevUserSeeder) → không tự tạo user

        var workspace = StudioWorkspace.Create("Studio Moonlight", ProducerUser);
        SetId(workspace, WorkspaceId);
        db.StudioWorkspaces.Add(workspace);
        db.WorkspaceMembers.Add(WorkspaceMember.Create(WorkspaceId, ProducerUser, WorkspaceRole.Producer));
        db.WorkspaceMembers.Add(WorkspaceMember.Create(WorkspaceId, WriterUser, WorkspaceRole.Writer));
        db.WorkspaceMembers.Add(WorkspaceMember.Create(WorkspaceId, EditorUser, WorkspaceRole.Editor));
        db.WorkspaceMembers.Add(WorkspaceMember.Create(WorkspaceId, ArtistUser, WorkspaceRole.Artist));
        // Letterer (tài khoản dev letterer@) là tuỳ chọn: có thì thêm để demo đủ nhiệm vụ Letterer; không có thì bỏ qua
        if (await db.Users.AnyAsync(u => u.Id == LettererUser, ct))
            db.WorkspaceMembers.Add(WorkspaceMember.Create(WorkspaceId, LettererUser, WorkspaceRole.Letterer));

        // Mock không cần API key; Gemini: Admin nhập key qua PUT /api/Admin/workspaces/{id}/ai-config
        db.WorkspaceAiConfigs.Add(WorkspaceAiConfig.Create(
            WorkspaceId, "Google Gemini", apiKey: null, monthlyTokenQuota: 1_000_000,
            allowedModelsJson: "[\"gemini-2.5-flash\",\"gemini-2.5-pro\"]"));

        var series = Series.Create(WorkspaceId, "Thành phố mưa", ReadingDirection.RightToLeft,
            "Thợ học việc An lần theo chiếc chìa khóa bạc bí ẩn giữa thành phố luôn có mưa.", "Mystery", "Manga");
        SetId(series, SeriesId);
        db.Series.Add(series);

        var bible = SeriesBible.Create(SeriesId, WorkspaceId, "Series Bible – Thành phố mưa");
        db.SeriesBibles.Add(bible);
        AddEntry(bible, BibleEntryCategory.Character, "AN", "An", AnDescription);
        AddEntry(bible, BibleEntryCategory.Character, "BA_LAN", "Bà Lan",
            "Bà Lan (60 tuổi) là chủ quán trà Giọt Mưa đối diện tiệm đồng hồ. Bà là người giữ chiếc chìa khóa bạc cho tới khi trao nó cho An ở Chương 5.");
        AddEntry(bible, BibleEntryCategory.Prop, "KHOA_BAC", "Chìa khóa bạc",
            "Chiếc chìa khóa bạc khắc hình giọt mưa. Từ đầu truyện đến hết Chương 4 nó nằm trong tay bà Lan; chỉ chuyển sang An ở Chương 5.");
        AddEntry(bible, BibleEntryCategory.Location, "QUAN_TRA", "Quán trà Giọt Mưa",
            "Quán trà nhỏ có mái hiên thấp, lúc nào cũng nghe tiếng mưa rơi trên mái.");
        AddEntry(bible, BibleEntryCategory.PlotFact, "MOC_CK5", "Mốc timeline chìa khóa bạc",
            "Chìa khóa bạc chỉ chuyển sang An ở Chương 5. Các chương 1–4 không được mô tả An cầm, dùng hoặc sở hữu chìa khóa bạc.");

        var chapter = Chapter.Create(SeriesId, 3, "Tiếng mưa đêm");
        SetId(chapter, ChapterId);
        db.Chapters.Add(chapter);
        var scene = Scene.Create(ChapterId, 1, "Tiệm đồng hồ trong đêm mưa",
            "An dọn tiệm đồng hồ lúc nửa đêm thì thấy bà Lan đứng ngoài hiên, nhìn chằm chằm vào ổ khóa chiếc tủ cũ.");
        SetId(scene, SceneId);
        db.Scenes.Add(scene);

        await db.SaveChangesAsync(ct);
        return true;
    }

    private static void AddEntry(SeriesBible bible, BibleEntryCategory category, string code, string name, string description)
    {
        var entry = bible.AddEntry(category, code, name, description);
        var snapshot = JsonSerializer.Serialize(new { Name = name, Description = description });
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(snapshot))).ToLowerInvariant();
        var revision = entry.AddRevision("Khởi tạo (dữ liệu mẫu demo AI)", snapshot, hash, effectiveFromChapterNumber: 1, isInitialVersion: true);
        // AI chỉ dùng bản thiết kế đã khoá: khoá sẵn để demo có nhân vật, địa điểm, đạo cụ trong context
        if (entry.IsVisualDesign) revision.Lock(ProducerUser);
    }

    private static void SetId(BaseEntity entity, Guid id) =>
        typeof(BaseEntity).GetProperty(nameof(BaseEntity.Id))!.SetValue(entity, id);
}