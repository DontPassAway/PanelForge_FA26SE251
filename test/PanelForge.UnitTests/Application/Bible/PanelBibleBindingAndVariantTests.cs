using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Features.AiAgents.Common;
using PanelForge.Application.Features.Bible.Queries.GetBibleEntriesAtChapter;
using PanelForge.Application.Features.Bible.Queries.GetPanelBible;
using PanelForge.Domain.Entities.Bible;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Entities.Workspaces;
using PanelForge.Domain.Enums;
using PanelForge.Infrastructure.Persistence;
using PanelForge.Infrastructure.Services;
using PanelForge.UnitTests.Common;
using Xunit;

namespace PanelForge.UnitTests.Application.Bible;

/// <summary>
/// BIB-15 (Q16, Q23, Q24):
/// (1) GetPanelBible trả bản theo panel_bible_variants nếu panel có bản riêng (vd: hồi tưởng),
///     ngược lại chapter_bible_bindings.bound_revision_id, kèm Model Sheet; ẩn is_crowd_at_bind.
/// (2) Đọc theo binding chương: chapter 5 giữ bản cũ sau khi bản thay thế được khoá Canon.
/// (3) Panel hồi tưởng hiện Model Sheet "Lúc nhỏ", Panel khác trong cùng chương hiện "Mặc định".
/// </summary>
public class PanelBibleBindingAndVariantTests
{
    private readonly PanelForgeDbContext _db = InMemoryDb.Create();
    private readonly Guid _seriesId = Guid.NewGuid();
    private readonly Guid _workspaceId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();

    private readonly Series _series;
    private readonly SeriesBible _bible;
    private readonly BibleEntry _hero;
    private readonly BibleEntryRevision _heroV1;
    private readonly BibleEntryRevision _heroV2Replacement;
    private readonly BibleEntryRevision _heroYoungVariant;

    public PanelBibleBindingAndVariantTests()
    {
        var ws = StudioWorkspace.Create("Test Studio", _userId);
        typeof(StudioWorkspace).GetProperty(nameof(StudioWorkspace.Id))?.SetValue(ws, _workspaceId);
        _db.StudioWorkspaces.Add(ws);

        _series = Series.Create(_workspaceId, "Action Manga", ReadingDirection.RightToLeft, "Mô tả test");
        typeof(Series).GetProperty(nameof(Series.Id))?.SetValue(_series, _seriesId);
        _db.Series.Add(_series);

        _bible = SeriesBible.Create(_seriesId, _workspaceId, "Series Bible");
        _hero = _bible.AddEntry(BibleEntryCategory.Character, "CHR-HERO", "Akira", "Dũng sĩ chính");

        // Biến thể mặc định: v1 khoá Canon
        _heroV1 = _hero.AddRevision("v1",
            JsonSerializer.Serialize(new { Name = "Akira", Description = "Akira trưởng thành", ReferenceImageUrl = "https://r2/akira-v1.png" }),
            "hash-v1", authorId: _userId, stageName: "Mặc định", isInitialVersion: true);
        _heroV1.SubmitConcept(_userId, "https://r2/akira-v1-concept.png", null);
        _heroV1.Lock(_userId);

        // Bản thay thế v2 cùng biến thể "Mặc định" (chưa khoá)
        _heroV2Replacement = _hero.AddRevision("v2",
            JsonSerializer.Serialize(new { Name = "Akira", Description = "Akira v2 nâng cấp", ReferenceImageUrl = "https://r2/akira-v2.png" }),
            "hash-v2", authorId: _userId, stageName: "Mặc định", supersedesRevisionId: _heroV1.Id);
        _heroV2Replacement.SubmitConcept(_userId, "https://r2/akira-v2-concept.png", null);

        // Biến thể riêng: "Lúc nhỏ"
        _heroYoungVariant = _hero.AddRevision("v1-young",
            JsonSerializer.Serialize(new { Name = "Akira", Description = "Akira thời thơ ấu", ReferenceImageUrl = "https://r2/akira-young.png" }),
            "hash-young", authorId: _userId, stageName: "Lúc nhỏ");
        _heroYoungVariant.SubmitConcept(_userId, "https://r2/akira-young-modelsheet.png", null);
        _heroYoungVariant.Lock(_userId);

        _db.SeriesBibles.Add(_bible);
        _db.BibleEntries.Add(_hero);
        _db.BibleEntryRevisions.AddRange(_heroV1, _heroV2Replacement, _heroYoungVariant);
        _db.SaveChanges();
    }

    [Fact]
    public async Task Chapter5_KeepsOldRevision_AfterReplacementIsLocked()
    {
        // 1. Tạo Chapter 5 và gắn bản v1 (chosen_by_writer = true hoặc đã gắn)
        var ch5 = Chapter.Create(_seriesId, 5, "Chương 5");
        _db.Chapters.Add(ch5);

        var ch5Binding = ChapterBibleBinding.Create(
            ch5.Id,
            _hero.Id,
            _heroV1.Id,
            ChapterBibleBindingSource.Speaker,
            chosenByWriter: true);
        _db.ChapterBibleBindings.Add(ch5Binding);
        await _db.SaveChangesAsync();

        var queryHandler = new GetBibleEntriesAtChapterQueryHandler(_db);

        // Trước khi v2 được khoá: Chapter 5 hiển thị bản v1
        var resultBefore = await queryHandler.Handle(
            new GetBibleEntriesAtChapterQuery(_seriesId, 5, CanonOnly: true), default);
        resultBefore.IsSuccess.Should().BeTrue();
        var heroBefore = resultBefore.Value!.Single(e => e.Id == _hero.Id);
        heroBefore.EffectiveRevision!.Id.Should().Be(_heroV1.Id);
        heroBefore.EffectiveRevision.VersionNumber.Should().Be(_heroV1.VersionNumber);

        // 2. Editor khoá bản thay thế v2 (supersedes v1)
        _heroV2Replacement.Lock(_userId);
        _heroV1.MarkSuperseded(_userId, 6, "Thay thế bởi v2");
        await _db.SaveChangesAsync();

        // Chạy InheritanceResolver để mô phỏng tác động chuỗi kế thừa
        var resolver = new ChapterCastInheritanceResolver(_db);
        await resolver.ResolveInheritedAsync(_seriesId, 1);
        await _db.SaveChangesAsync();

        // 3. Sau khi v2 đã khoá Canon: Chapter 5 VẪN GIỮ bản cũ v1 vì binding của Chapter 5 không tự chuyển
        var resultAfter = await queryHandler.Handle(
            new GetBibleEntriesAtChapterQuery(_seriesId, 5, CanonOnly: true), default);
        resultAfter.IsSuccess.Should().BeTrue();
        var heroAfter = resultAfter.Value!.Single(e => e.Id == _hero.Id);
        heroAfter.EffectiveRevision!.Id.Should().Be(_heroV1.Id,
            "chương 5 giữ bản cũ sau khi bản thay thế được khoá (Q23, BIB-15)");
    }

    [Fact]
    public async Task GetPanelBible_FlashbackPanel_ShowsYoungVariant_WhileAnotherPanelShowsDefaultVariant()
    {
        // Tạo Chapter, Page và 2 Panels trong cùng một chương
        var chapter = Chapter.Create(_seriesId, 1, "Chương 1");
        _db.Chapters.Add(chapter);

        var page = Page.CreateNew(chapter.Id, 1, LayoutFormat.StandardPage, 1200, 1800, 300);
        _db.Pages.Add(page);

        var scene = Scene.Create(chapter.Id, 1);
        _db.Scenes.Add(scene);

        var panel1Flashback = Panel.Create(page.Id, 1, 1, boundingBox: "{}", sceneId: scene.Id, direction: "Panel 1: Hồi tưởng thơ ấu");
        var panel2Present = Panel.Create(page.Id, 2, 2, boundingBox: "{}", sceneId: scene.Id, direction: "Panel 2: Hiện tại");
        _db.Panels.AddRange(panel1Flashback, panel2Present);

        // Gắn binding mặc định của chapter cho hero = v1 ("Mặc định")
        var chapterBinding = ChapterBibleBinding.Create(
            chapter.Id,
            _hero.Id,
            _heroV1.Id,
            ChapterBibleBindingSource.Speaker,
            chosenByWriter: false);
        _db.ChapterBibleBindings.Add(chapterBinding);

        // Panel 1 (hồi tưởng) có biến thể riêng trong panel_bible_variants = "Lúc nhỏ"
        var panel1Variant = PanelBibleVariant.Create(
            panel1Flashback.Id,
            _hero.Id,
            _heroYoungVariant.Id,
            _userId);
        _db.PanelBibleVariants.Add(panel1Variant);

        // Cả 2 panel đều có dòng thoại của Akira
        var line1 = ScriptLine.Create(scene.Id, 1, "Hồi đó tôi còn nhỏ...", null, speakerLabel: "Akira");
        line1.AssignToPanel(panel1Flashback.Id);
        var line2 = ScriptLine.Create(scene.Id, 2, "Bây giờ tôi đã lớn!", null, speakerLabel: "Akira");
        line2.AssignToPanel(panel2Present.Id);
        _db.ScriptLines.AddRange(line1, line2);

        await _db.SaveChangesAsync();

        var handler = new GetPanelBibleQueryHandler(_db);

        // 1. Panel 1 (hồi tưởng) phải trả biến thể "Lúc nhỏ" kèm Model Sheet tương ứng
        var resultPanel1 = await handler.Handle(new GetPanelBibleQuery(panel1Flashback.Id), default);
        resultPanel1.IsSuccess.Should().BeTrue();
        var heroInPanel1 = resultPanel1.Value!.Single(e => e.Id == _hero.Id);
        heroInPanel1.EffectiveRevision!.Id.Should().Be(_heroYoungVariant.Id);
        heroInPanel1.EffectiveRevision.StageName.Should().Be("Lúc nhỏ");
        heroInPanel1.ReferenceImageUrl.Should().Be("https://r2/akira-young-modelsheet.png");

        // 2. Panel 2 (cùng chương, không có variant riêng) phải dùng bản mặc định của chapter ("Mặc định" v1)
        var resultPanel2 = await handler.Handle(new GetPanelBibleQuery(panel2Present.Id), default);
        resultPanel2.IsSuccess.Should().BeTrue();
        var heroInPanel2 = resultPanel2.Value!.Single(e => e.Id == _hero.Id);
        heroInPanel2.EffectiveRevision!.Id.Should().Be(_heroV1.Id);
        heroInPanel2.EffectiveRevision.StageName.Should().Be("Mặc định");
        heroInPanel2.ReferenceImageUrl.Should().Be("https://r2/akira-v1-concept.png");
    }

    [Fact]
    public async Task GetPanelBible_HidesEntries_WithIsCrowdAtBind()
    {
        var chapter = Chapter.Create(_seriesId, 2, "Chương 2");
        _db.Chapters.Add(chapter);

        var page = Page.CreateNew(chapter.Id, 1, LayoutFormat.StandardPage, 1200, 1800, 300);
        _db.Pages.Add(page);

        var scene = Scene.Create(chapter.Id, 1);
        _db.Scenes.Add(scene);

        var panel = Panel.Create(page.Id, 1, 1, boundingBox: "{}", sceneId: scene.Id, direction: "Panel đám đông");
        _db.Panels.Add(panel);

        // Tạo thêm một nhân vật quần chúng
        var crowdEntry = _bible.AddEntry(BibleEntryCategory.Character, "CHR-CROWD", "Dân làng A", "Người qua đường");
        var crowdRev = crowdEntry.AddRevision("v1", "{}", "hash-crowd", stageName: "Mặc định", isInitialVersion: true);
        crowdRev.Lock(_userId);
        _db.BibleEntries.Add(crowdEntry);
        _db.BibleEntryRevisions.Add(crowdRev);

        // Đánh dấu is_crowd_at_bind = true trong chapter_bible_bindings
        var crowdBinding = ChapterBibleBinding.Create(
            chapter.Id,
            crowdEntry.Id,
            crowdRev.Id,
            ChapterBibleBindingSource.Speaker,
            chosenByWriter: false,
            isCrowdAtBind: true);
        _db.ChapterBibleBindings.Add(crowdBinding);

        // Panel có người nói là Dân làng A
        var line = ScriptLine.Create(scene.Id, 1, "Cứu với!", null, speakerLabel: "Dân làng A");
        line.AssignToPanel(panel.Id);
        _db.ScriptLines.Add(line);

        await _db.SaveChangesAsync();

        var handler = new GetPanelBibleQueryHandler(_db);
        var result = await handler.Handle(new GetPanelBibleQuery(panel.Id), default);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Should().BeEmpty("thực thể có is_crowd_at_bind phải bị ẩn khỏi GetPanelBible (BIB-15)");
    }

    [Fact]
    public async Task GetPanelBible_IncludesEntities_MentionedViaHighlightedRef()
    {
        var chapter = Chapter.Create(_seriesId, 3, "Chương 3");
        _db.Chapters.Add(chapter);

        var page = Page.CreateNew(chapter.Id, 1, LayoutFormat.StandardPage, 1200, 1800, 300);
        _db.Pages.Add(page);

        var scene = Scene.Create(chapter.Id, 1);
        _db.Scenes.Add(scene);

        var panel = Panel.Create(page.Id, 1, 1, boundingBox: "{}", sceneId: scene.Id, direction: "Panel bối cảnh");
        _db.Panels.Add(panel);

        // Địa điểm: Tháp cổ
        var tower = _bible.AddEntry(BibleEntryCategory.Location, "LOC-TOWER", "Tháp Cổ", "Ngọn tháp kỳ bí");
        var towerRev = tower.AddRevision("v1",
            JsonSerializer.Serialize(new { Name = "Tháp Cổ", Description = "Tháp đá rêu phong", ReferenceImageUrl = "https://r2/tower.png" }),
            "hash-tower", stageName: "Mặc định", isInitialVersion: true);
        towerRev.SubmitConcept(_userId, "https://r2/tower-concept.png", null);
        towerRev.Lock(_userId);
        _db.BibleEntries.Add(tower);
        _db.BibleEntryRevisions.Add(towerRev);

        var towerBinding = ChapterBibleBinding.Create(
            chapter.Id,
            tower.Id,
            towerRev.Id,
            ChapterBibleBindingSource.Highlight);
        _db.ChapterBibleBindings.Add(towerBinding);

        // Dòng action không có người nói, nhưng có script_line_bible_links tham chiếu Tháp Cổ
        var actionLine = ScriptLine.Create(scene.Id, 1, null, "Họ tiến về phía Tháp Cổ.");
        actionLine.AssignToPanel(panel.Id);
        _db.ScriptLines.Add(actionLine);

        var link = ScriptLineBibleLink.Create(actionLine.Id, tower.Id, "Tháp Cổ");
        _db.ScriptLineBibleLinks.Add(link);

        await _db.SaveChangesAsync();

        var handler = new GetPanelBibleQueryHandler(_db);
        var result = await handler.Handle(new GetPanelBibleQuery(panel.Id), default);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Should().HaveCount(1);
        var loc = result.Value!.Single();
        loc.Id.Should().Be(tower.Id);
        loc.Name.Should().Be("Tháp Cổ");
        loc.ReferenceImageUrl.Should().Be("https://r2/tower-concept.png");
    }

    [Fact]
    public async Task AiSnapshotBuilder_BuildBibleAsync_ReadsFromChapterBinding()
    {
        var chapter = Chapter.Create(_seriesId, 1, "Chương 1");
        _db.Chapters.Add(chapter);

        var chapterBinding = ChapterBibleBinding.Create(
            chapter.Id,
            _hero.Id,
            _heroV1.Id,
            ChapterBibleBindingSource.Speaker);
        _db.ChapterBibleBindings.Add(chapterBinding);
        await _db.SaveChangesAsync();

        var aiBible = await AiSnapshotBuilder.BuildBibleAsync(_db, chapter, default);

        aiBible.Should().NotBeNull();
        var heroAi = aiBible!.Single(b => b.EntryId == _hero.Id);
        heroAi.RevisionId.Should().Be(_heroV1.Id);
        heroAi.RevisionVersion.Should().Be(_heroV1.VersionNumber);
    }
}
