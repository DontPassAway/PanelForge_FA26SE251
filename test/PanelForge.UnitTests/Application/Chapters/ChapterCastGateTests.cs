using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using PanelForge.AI.Contracts;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.Bible.Commands.SetChapterCastRevision;
using PanelForge.Application.Features.Chapters.Commands.ApproveChapter;
using PanelForge.Application.Features.Chapters.Commands.FreezeScript;
using PanelForge.Application.Features.Chapters.Commands.ReopenChapter;
using PanelForge.Application.Features.Chapters.Common;
using PanelForge.Application.Features.Chapters.Queries.GetChapterCast;
using PanelForge.Application.Features.Chapters.Services;
using PanelForge.Domain.Entities.Bible;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Entities.Workspaces;
using PanelForge.Domain.Enums;
using PanelForge.Infrastructure.Persistence;
using PanelForge.Infrastructure.Services;
using PanelForge.UnitTests.Common;
using Xunit;
using DomainSeries = PanelForge.Domain.Entities.Content.Series;

namespace PanelForge.UnitTests.Application.Chapters;

/// <summary>
/// Toàn bộ Unit Tests cho BIB-14:
/// - ChapterCastGate hai mốc (SubmitScriptCommand & FreezeChapterScriptCommand)
/// - Đóng băng is_crowd_at_bind và bound_revision_id
/// - Kế thừa Ch1 -> Ch2 (v2 chờ khoá Canon) -> Ch3 kế thừa v2, Ch1 giữ v1
/// - Panel hồi tưởng variant chưa khoá -> 409
/// - Sửa binding khi Frozen -> 409
/// - Mở lại không tự kế thừa lại
/// - Gỡ cờ quần chúng sau Gate 1 không mở lại vướng của chương đã chốt
/// - Gắn cờ ảnh hưởng Panel khi chốt lại (SCR-01)
/// - ChapterSnapshotBuilder ghi cast map + panel variants + frozen_script_version_id
/// </summary>
public class ChapterCastGateTests
{
    private readonly PanelForgeDbContext _db = InMemoryDb.Create();
    private readonly Mock<IWorkspaceAuthorizationService> _auth = new();
    private readonly Mock<IPageRepository> _pages = new();
    private readonly ChapterCastInheritanceResolver _resolver;
    private readonly ChapterCastService _castService;
    private readonly ChapterCastGate _castGate;

    private readonly StudioWorkspace _studio;
    private readonly DomainSeries _series;
    private readonly SeriesBible _bible;

    private readonly Guid _writerId = Guid.NewGuid();
    private readonly Guid _producerId = Guid.NewGuid();
    private readonly Guid _editorId = Guid.NewGuid();

    public ChapterCastGateTests()
    {
        _resolver = new ChapterCastInheritanceResolver(_db);
        _castService = new ChapterCastService(_db, _resolver);
        _castGate = new ChapterCastGate(_db, _castService, _resolver);

        _studio = StudioWorkspace.Create("Test Studio", _producerId);
        _db.StudioWorkspaces.Add(_studio);

        _series = DomainSeries.Create(_studio.Id, "Series BIB-14", ReadingDirection.RightToLeft);
        _db.Series.Add(_series);

        _bible = SeriesBible.Create(_series.Id, _studio.Id, "Bible BIB-14");
        _db.SeriesBibles.Add(_bible);

        _db.WorkspaceMembers.Add(WorkspaceMember.Create(_studio.Id, _writerId, WorkspaceRole.Writer));
        _db.WorkspaceMembers.Add(WorkspaceMember.Create(_studio.Id, _producerId, WorkspaceRole.Producer));
        _db.WorkspaceMembers.Add(WorkspaceMember.Create(_studio.Id, _editorId, WorkspaceRole.Editor));
        _db.SaveChanges();

        _auth.Setup(a => a.GetMemberRoleAsync(_writerId, _studio.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(WorkspaceRole.Writer);
        _auth.Setup(a => a.GetMemberRoleAsync(_producerId, _studio.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(WorkspaceRole.Producer);
        _auth.Setup(a => a.GetMemberRoleAsync(_editorId, _studio.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(WorkspaceRole.Editor);
    }

    [Fact]
    public async Task SubmitScript_WhenDraftStubExists_Returns409Conflict()
    {
        var chapter = Chapter.Create(_series.Id, 1, "Chương 1");
        _db.Chapters.Add(chapter);
        var scene = Scene.Create(chapter.Id, 1, "Cảnh 1");
        _db.Scenes.Add(scene);

        // Tạo mục stub chưa định nghĩa
        var stub = BibleEntry.CreateStub(_bible.Id, BibleEntryCategory.Character, "Kage");
        _db.BibleEntries.Add(stub);
        _db.BibleEntryNames.Add(BibleEntryName.Create(_bible.Id, "Kage", stub.Id, isPrimary: true));

        // Script line nhắc tới Kage
        var line = ScriptLine.Create(scene.Id, 1, "Tôi là Kage", "Kage", stub.Id);
        _db.ScriptLines.Add(line);
        await _db.SaveChangesAsync();

        var handler = new ScriptLifecycleCommandHandler(_db, _auth.Object, _castGate);
        var result = await handler.Handle(new SubmitScriptCommand(chapter.Id, _writerId), default);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        result.ErrorMessage.Should().Contain("Kage");
        result.ErrorMessage.Should().Contain("chờ định nghĩa");
    }

    [Fact]
    public async Task SubmitScript_WhenDraftStubMarkedAsCrowd_Passes()
    {
        var chapter = Chapter.Create(_series.Id, 1, "Chương 1");
        _db.Chapters.Add(chapter);
        var scene = Scene.Create(chapter.Id, 1, "Cảnh 1");
        _db.Scenes.Add(scene);

        var stub = BibleEntry.CreateStub(_bible.Id, BibleEntryCategory.Character, "Dân Làng");
        stub.SetCrowd(true);
        _db.BibleEntries.Add(stub);
        _db.BibleEntryNames.Add(BibleEntryName.Create(_bible.Id, "Dân Làng", stub.Id, isPrimary: true));

        var line = ScriptLine.Create(scene.Id, 1, "Cứu với!", "Dân Làng", stub.Id);
        _db.ScriptLines.Add(line);
        await _db.SaveChangesAsync();

        var handler = new ScriptLifecycleCommandHandler(_db, _auth.Object, _castGate);
        var result = await handler.Handle(new SubmitScriptCommand(chapter.Id, _writerId), default);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ScriptStatus.Should().Be(ScriptStatus.InReview);
    }

    [Fact]
    public async Task SubmitScript_WhenWriterDoesNotChangeRevision_PassesWithInheritedRevision()
    {
        // Ch1 chốt với v1 CanonLocked
        var hero = _bible.AddEntry(BibleEntryCategory.Character, "HERO", "Hero", "Anh hùng");
        var v1 = hero.AddRevision("v1", "{}", "hash-v1", authorId: _writerId, isInitialVersion: true);
        v1.SubmitConcept(_writerId, "https://r2/v1.png", null);
        v1.Lock(_editorId);
        _db.BibleEntries.Add(hero);
        _db.BibleEntryRevisions.Add(v1);

        var ch1 = Chapter.Create(_series.Id, 1, "Chương 1");
        _db.Chapters.Add(ch1);
        var b1 = ChapterBibleBinding.Create(ch1.Id, hero.Id, v1.Id, ChapterBibleBindingSource.Speaker, chosenByWriter: false);
        _db.ChapterBibleBindings.Add(b1);

        // Ch2 kịch bản nhắc tới Hero nhưng Writer không đổi bản (chosenByWriter = false)
        var ch2 = Chapter.Create(_series.Id, 2, "Chương 2");
        _db.Chapters.Add(ch2);
        var scene2 = Scene.Create(ch2.Id, 1, "Cảnh 1");
        _db.Scenes.Add(scene2);
        var line2 = ScriptLine.Create(scene2.Id, 1, "Tôi trở lại!", "Hero", hero.Id);
        _db.ScriptLines.Add(line2);
        _db.BibleEntryNames.Add(BibleEntryName.Create(_bible.Id, "Hero", hero.Id, isPrimary: true));
        await _db.SaveChangesAsync();

        var handler = new ScriptLifecycleCommandHandler(_db, _auth.Object, _castGate);
        var result = await handler.Handle(new SubmitScriptCommand(ch2.Id, _writerId), default);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ScriptStatus.Should().Be(ScriptStatus.InReview);

        // Binding của Ch2 được kế thừa v1
        var binding2 = await _db.ChapterBibleBindings.FirstOrDefaultAsync(b => b.ChapterId == ch2.Id && b.BibleEntryId == hero.Id);
        binding2.Should().NotBeNull();
        binding2!.BoundRevisionId.Should().Be(v1.Id);
        binding2.ChosenByWriter.Should().BeFalse();
    }

    [Fact]
    public async Task Gate1_SequentialInheritanceAndSupersedeFlow()
    {
        // 1. Ch1 chốt với v1 CanonLocked
        var hero = _bible.AddEntry(BibleEntryCategory.Character, "NARUTO", "Naruto", "Ninja");
        var v1 = hero.AddRevision("v1", "{}", "hash-v1", authorId: _writerId, isInitialVersion: true, stageName: "Mặc định");
        v1.SubmitConcept(_writerId, "https://r2/v1.png", null);
        v1.Lock(_editorId);
        _db.BibleEntries.Add(hero);
        _db.BibleEntryRevisions.Add(v1);
        _db.BibleEntryNames.Add(BibleEntryName.Create(_bible.Id, "Naruto", hero.Id, isPrimary: true));

        var ch1 = Chapter.Create(_series.Id, 1, "Chương 1");
        _db.Chapters.Add(ch1);
        var sc1 = Scene.Create(ch1.Id, 1, "Scene 1");
        _db.Scenes.Add(sc1);
        _db.ScriptLines.Add(ScriptLine.Create(sc1.Id, 1, "Dattebayo!", "Naruto", hero.Id));
        await _db.SaveChangesAsync();

        var submitHandler = new ScriptLifecycleCommandHandler(_db, _auth.Object, _castGate);
        var freezeHandler = new FreezeChapterScriptCommandHandler(_db, _castGate);

        await submitHandler.Handle(new SubmitScriptCommand(ch1.Id, _writerId), default);
        var freezeCh1 = await freezeHandler.Handle(new FreezeChapterScriptCommand(ch1.Id, _producerId), default);
        freezeCh1.IsSuccess.Should().BeTrue();
        ch1.ScriptStatus.Should().Be(ScriptStatus.Frozen);

        var ch1Binding = await _db.ChapterBibleBindings.SingleAsync(b => b.ChapterId == ch1.Id);
        ch1Binding.BoundRevisionId.Should().Be(v1.Id);

        // 2. Writer tạo v2 từ Ch2 (chưa được khoá Canon)
        var v2 = hero.AddRevision("v2", "{}", "hash-v2", authorId: _writerId, stageName: "Mặc định", reviewStatus: BibleRevisionStatus.Draft);
        _db.BibleEntryRevisions.Add(v2);

        var ch2 = Chapter.Create(_series.Id, 2, "Chương 2");
        _db.Chapters.Add(ch2);
        var sc2 = Scene.Create(ch2.Id, 1, "Scene 2");
        _db.Scenes.Add(sc2);
        _db.ScriptLines.Add(ScriptLine.Create(sc2.Id, 1, "Rasengan!", "Naruto", hero.Id));
        await _db.SaveChangesAsync();

        // Gán v2 cho Ch2 (cần recompute trước để sinh binding kế thừa từ Ch1)
        await _castService.RecomputeAsync(ch2.Id, _writerId);
        await _db.SaveChangesAsync();
        var setRevHandler = new SetChapterCastRevisionCommandHandler(_db, _resolver);
        var setRes = await setRevHandler.Handle(new SetChapterCastRevisionCommand(ch2.Id, hero.Id, v2.Id, _writerId), default);
        setRes.IsSuccess.Should().BeTrue();

        // Writer gửi duyệt Ch2 thành công (Submit không đòi CanonLocked)
        var submitCh2 = await submitHandler.Handle(new SubmitScriptCommand(ch2.Id, _writerId), default);
        submitCh2.IsSuccess.Should().BeTrue();

        // Gate 1 trên Ch2 bị 409 vì v2 chưa CanonLocked
        var freezeCh2Blocked = await freezeHandler.Handle(new FreezeChapterScriptCommand(ch2.Id, _producerId), default);
        freezeCh2Blocked.IsSuccess.Should().BeFalse();
        freezeCh2Blocked.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        freezeCh2Blocked.ErrorMessage.Should().Contain("chưa được khoá Canon");

        // 3. Artist nộp concept và Editor khoá v2
        v2.SubmitConcept(_writerId, "https://r2/v2.png", null);
        v2.Lock(_editorId);
        await _db.SaveChangesAsync();

        // Sau khi khoá Canon, Gate 1 trên Ch2 thành công
        var freezeCh2Success = await freezeHandler.Handle(new FreezeChapterScriptCommand(ch2.Id, _producerId), default);
        freezeCh2Success.IsSuccess.Should().BeTrue();
        ch2.ScriptStatus.Should().Be(ScriptStatus.Frozen);

        // 4. Ch3 kế thừa v2; Ch1 vẫn giữ nguyên v1
        var ch3 = Chapter.Create(_series.Id, 3, "Chương 3");
        _db.Chapters.Add(ch3);
        var sc3 = Scene.Create(ch3.Id, 1, "Scene 3");
        _db.Scenes.Add(sc3);
        _db.ScriptLines.Add(ScriptLine.Create(sc3.Id, 1, "Chào mọi người!", "Naruto", hero.Id));
        await _db.SaveChangesAsync();

        await submitHandler.Handle(new SubmitScriptCommand(ch3.Id, _writerId), default);

        var ch3Binding = await _db.ChapterBibleBindings.SingleAsync(b => b.ChapterId == ch3.Id && b.BibleEntryId == hero.Id);
        ch3Binding.BoundRevisionId.Should().Be(v2.Id, "Ch3 kế thừa bản CanonLocked mới nhất từ Ch2");

        ch1Binding = await _db.ChapterBibleBindings.SingleAsync(b => b.ChapterId == ch1.Id);
        ch1Binding.BoundRevisionId.Should().Be(v1.Id, "Ch1 vẫn giữ nguyên v1 đã chốt");
    }

    [Fact]
    public async Task Gate1_WhenPanelVariantNotLocked_Returns409Conflict()
    {
        var hero = _bible.AddEntry(BibleEntryCategory.Character, "SASUKE", "Sasuke", "Ninja");
        var vMain = hero.AddRevision("v1", "{}", "hash-main", authorId: _writerId, isInitialVersion: true, stageName: "Mặc định");
        vMain.SubmitConcept(_writerId, "https://r2/main.png", null);
        vMain.Lock(_editorId);

        var vChildhood = hero.AddRevision("v-child", "{}", "hash-child", authorId: _writerId, stageName: "Lúc nhỏ", reviewStatus: BibleRevisionStatus.Draft);
        _db.BibleEntries.Add(hero);
        _db.BibleEntryRevisions.AddRange(vMain, vChildhood);
        _db.BibleEntryNames.Add(BibleEntryName.Create(_bible.Id, "Sasuke", hero.Id, isPrimary: true));

        var chapter = Chapter.Create(_series.Id, 1, "Chương 1");
        _db.Chapters.Add(chapter);
        var scene = Scene.Create(chapter.Id, 1, "Cảnh 1");
        _db.Scenes.Add(scene);

        var page = Page.CreateNew(chapter.Id, 1, default, 1200, 1800, 300);
        _db.Pages.Add(page);
        var panel = Panel.Create(page.Id, 1, 1);
        _db.Panels.Add(panel);

        // Panel đặt biến thể "Lúc nhỏ" chưa khoá
        var pv = PanelBibleVariant.Create(panel.Id, hero.Id, vChildhood.Id, _writerId);
        _db.PanelBibleVariants.Add(pv);

        var line = ScriptLine.Create(scene.Id, 1, "Hồi tưởng...", "Sasuke", hero.Id);
        line.AssignToPanel(panel.Id);
        _db.ScriptLines.Add(line);
        await _db.SaveChangesAsync();

        var submitHandler = new ScriptLifecycleCommandHandler(_db, _auth.Object, _castGate);
        await submitHandler.Handle(new SubmitScriptCommand(chapter.Id, _writerId), default);

        var freezeHandler = new FreezeChapterScriptCommandHandler(_db, _castGate);
        var freezeResult = await freezeHandler.Handle(new FreezeChapterScriptCommand(chapter.Id, _producerId), default);

        freezeResult.IsSuccess.Should().BeFalse();
        freezeResult.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        freezeResult.ErrorMessage.Should().Contain("Lúc nhỏ");
        freezeResult.ErrorMessage.Should().Contain("Panel 1.1");
    }

    [Fact]
    public async Task Gate1_WhenAllConditionsMet_FreezesChapterAndLocksBindings()
    {
        var hero = _bible.AddEntry(BibleEntryCategory.Character, "KAKASHI", "Kakashi", "Ninja");
        var v1 = hero.AddRevision("v1", "{}", "hash-k1", authorId: _writerId, isInitialVersion: true, stageName: "Mặc định");
        v1.SubmitConcept(_writerId, "https://r2/k1.png", null);
        v1.Lock(_editorId);
        _db.BibleEntries.Add(hero);
        _db.BibleEntryRevisions.Add(v1);
        _db.BibleEntryNames.Add(BibleEntryName.Create(_bible.Id, "Kakashi", hero.Id, isPrimary: true));

        var chapter = Chapter.Create(_series.Id, 1, "Chương 1");
        _db.Chapters.Add(chapter);
        var scene = Scene.Create(chapter.Id, 1, "Cảnh 1");
        _db.Scenes.Add(scene);
        _db.ScriptLines.Add(ScriptLine.Create(scene.Id, 1, "Chidori!", "Kakashi", hero.Id));
        await _db.SaveChangesAsync();

        var submitHandler = new ScriptLifecycleCommandHandler(_db, _auth.Object, _castGate);
        await submitHandler.Handle(new SubmitScriptCommand(chapter.Id, _writerId), default);

        var freezeHandler = new FreezeChapterScriptCommandHandler(_db, _castGate);
        var freezeResult = await freezeHandler.Handle(new FreezeChapterScriptCommand(chapter.Id, _producerId), default);

        freezeResult.IsSuccess.Should().BeTrue();
        chapter.ScriptStatus.Should().Be(ScriptStatus.Frozen);
        chapter.IsScriptFrozen.Should().BeTrue();
        chapter.FrozenScriptVersionId.Should().NotBeNull();

        var binding = await _db.ChapterBibleBindings.SingleAsync(b => b.ChapterId == chapter.Id);
        binding.IsCrowdAtBind.Should().BeFalse();
        binding.BoundRevisionId.Should().Be(v1.Id);
    }

    [Fact]
    public async Task SetChapterCastRevision_WhenChapterIsFrozen_Returns409Conflict()
    {
        var hero = _bible.AddEntry(BibleEntryCategory.Character, "JIRAIYA", "Jiraiya", "Sannin");
        var v1 = hero.AddRevision("v1", "{}", "hash-j1", authorId: _writerId, isInitialVersion: true);
        v1.SubmitConcept(_writerId, "https://r2/j1.png", null);
        v1.Lock(_editorId);
        var v2 = hero.AddRevision("v2", "{}", "hash-j2", authorId: _writerId);
        _db.BibleEntries.Add(hero);
        _db.BibleEntryRevisions.AddRange(v1, v2);

        var chapter = Chapter.Create(_series.Id, 1, "Chương 1");
        _db.Chapters.Add(chapter);
        chapter.SubmitScript(_writerId);
        chapter.StartProduction(_producerId, "hash-frozen");
        await _db.SaveChangesAsync();

        var handler = new SetChapterCastRevisionCommandHandler(_db, _resolver);
        var result = await handler.Handle(new SetChapterCastRevisionCommand(chapter.Id, hero.Id, v2.Id, _writerId), default);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        result.ErrorMessage.Should().Contain("đã đóng băng");
    }

    [Fact]
    public async Task ReopenChapter_DoesNotReinheritToNewerRevision()
    {
        var hero = _bible.AddEntry(BibleEntryCategory.Character, "TSUNADE", "Tsunade", "Sannin");
        var v1 = hero.AddRevision("v1", "{}", "hash-t1", authorId: _writerId, isInitialVersion: true);
        v1.SubmitConcept(_writerId, "https://r2/t1.png", null);
        v1.Lock(_editorId);
        _db.BibleEntries.Add(hero);
        _db.BibleEntryRevisions.Add(v1);

        // Ch1 chốt với v1
        var ch1 = Chapter.Create(_series.Id, 1, "Chương 1");
        _db.Chapters.Add(ch1);
        var sc1 = Scene.Create(ch1.Id, 1, "Cảnh 1");
        _db.Scenes.Add(sc1);
        _db.ScriptLines.Add(ScriptLine.Create(sc1.Id, 1, "Bắt đầu", "Tsunade", hero.Id));
        _db.ChapterBibleBindings.Add(ChapterBibleBinding.Create(ch1.Id, hero.Id, v1.Id, ChapterBibleBindingSource.Speaker, chosenByWriter: false));
        ch1.SubmitScript(_writerId);
        ch1.StartProduction(_producerId, "hash-ch1");

        // Sau đó có v2
        var v2 = hero.AddRevision("v2", "{}", "hash-t2", authorId: _writerId);
        v2.SubmitConcept(_writerId, "https://r2/t2.png", null);
        v2.Lock(_editorId);
        _db.BibleEntryRevisions.Add(v2);

        // Producer mở lại Ch1
        ch1.RequestScriptReopen(_producerId, "Producer xin mở lại để sửa kịch bản");
        ch1.ApproveScriptReopen(_producerId);
        await _db.SaveChangesAsync();

        // Chạy lại ResolveInheritedAsync
        await _resolver.ResolveInheritedAsync(_series.Id, 1, default);

        var ch1Binding = await _db.ChapterBibleBindings.SingleAsync(b => b.ChapterId == ch1.Id);
        ch1Binding.BoundRevisionId.Should().Be(v1.Id, "Chương đã chốt không tự kế thừa lại khi mở lại");
    }

    [Fact]
    public async Task UnsetCrowdAfterGate1_DoesNotAffectFrozenChapter()
    {
        var mob = _bible.AddEntry(BibleEntryCategory.Character, "MOB", "Quái vật", "Mục quần chúng");
        mob.SetCrowd(true);
        _db.BibleEntries.Add(mob);

        var chapter = Chapter.Create(_series.Id, 1, "Chương 1");
        _db.Chapters.Add(chapter);
        var binding = ChapterBibleBinding.Create(chapter.Id, mob.Id, null, ChapterBibleBindingSource.Speaker, isCrowdAtBind: true);
        _db.ChapterBibleBindings.Add(binding);
        chapter.SubmitScript(_writerId);
        chapter.StartProduction(_producerId, "hash-frozen");
        await _db.SaveChangesAsync();

        // Sau Gate 1, Writer gỡ cờ quần chúng trên mục Bible
        mob.SetCrowd(false);
        await _db.SaveChangesAsync();

        // Chạy recompute
        await _castService.RecomputeAsync(chapter.Id, _writerId, default);

        var reloaded = await _db.ChapterBibleBindings.SingleAsync(b => b.ChapterId == chapter.Id);
        reloaded.IsCrowdAtBind.Should().BeTrue("is_crowd_at_bind của chương đã chốt được đóng băng, không bị đổi");
    }

    [Fact]
    public async Task RefreezeAfterReopen_FlagsImpactedPanelsViaScr01()
    {
        var hero = _bible.AddEntry(BibleEntryCategory.Character, "SHIKAMARU", "Shikamaru", "Ninja");
        var v1 = hero.AddRevision("v1", "{}", "hash-s1", authorId: _writerId, isInitialVersion: true);
        v1.SubmitConcept(_writerId, "https://r2/s1.png", null);
        v1.Lock(_editorId);
        _db.BibleEntries.Add(hero);
        _db.BibleEntryRevisions.Add(v1);
        _db.BibleEntryNames.Add(BibleEntryName.Create(_bible.Id, "Shikamaru", hero.Id, isPrimary: true));

        var chapter = Chapter.Create(_series.Id, 1, "Chương 1");
        _db.Chapters.Add(chapter);
        var scene = Scene.Create(chapter.Id, 1, "Cảnh 1");
        _db.Scenes.Add(scene);

        var page = Page.CreateNew(chapter.Id, 1, default, 1200, 1800, 300);
        _db.Pages.Add(page);
        var panel = Panel.Create(page.Id, 1, 1);
        _db.Panels.Add(panel);

        var line = ScriptLine.Create(scene.Id, 1, "Thật phiền phức...", "Shikamaru", hero.Id);
        line.AssignToPanel(panel.Id);
        _db.ScriptLines.Add(line);

        // Tạo Assignment & StageProgress cho panel
        var stageId = Guid.NewGuid();
        var assignment = Assignment.Create(_series.Id, chapter.Id, WorkflowEntityType.Panel, panel.Id, stageId, _writerId, WorkspaceRole.Artist, "Vẽ panel");
        _db.Assignments.Add(assignment);
        var stageProgress = StageProgress.Begin(panel.Id, stageId);
        _db.StageProgresses.Add(stageProgress);
        await _db.SaveChangesAsync();

        var submitHandler = new ScriptLifecycleCommandHandler(_db, _auth.Object, _castGate);
        await submitHandler.Handle(new SubmitScriptCommand(chapter.Id, _writerId), default);

        var freezeHandler = new FreezeChapterScriptCommandHandler(_db, _castGate);
        await freezeHandler.Handle(new FreezeChapterScriptCommand(chapter.Id, _producerId), default);

        // Producer mở lại kịch bản
        chapter.RequestScriptReopen(_producerId, "Cần sửa lại thoại");
        chapter.ApproveScriptReopen(_producerId);
        await _db.SaveChangesAsync();

        var reopenSince = chapter.ScriptReopenRequestedAt!.Value;

        // Writer sửa dòng kịch bản trên panel
        line.UpdateContent("Chiến lược mới!", null, hero.Id);
        typeof(ScriptLine).GetProperty(nameof(ScriptLine.UpdatedAt))?.SetValue(line, reopenSince.AddSeconds(5));
        await _db.SaveChangesAsync();

        // Writer nộp lại và Producer chốt lại
        await submitHandler.Handle(new SubmitScriptCommand(chapter.Id, _writerId), default);
        var refreezeResult = await freezeHandler.Handle(new FreezeChapterScriptCommand(chapter.Id, _producerId), default);

        refreezeResult.IsSuccess.Should().BeTrue();

        // Kiểm tra SCR-01: assignment và stageProgress được gắn cờ tác động
        var reloadedAssignment = await _db.Assignments.SingleAsync(a => a.Id == assignment.Id);
        reloadedAssignment.HasScriptImpact.Should().BeTrue();
        reloadedAssignment.ImpactedScriptLineId.Should().Be(line.Id);

        var reloadedStageProgress = await _db.StageProgresses.SingleAsync(sp => sp.Id == stageProgress.Id);
        reloadedStageProgress.HasScriptImpact.Should().BeTrue();
        reloadedStageProgress.ImpactedScriptLineId.Should().Be(line.Id);
    }

    [Fact]
    public async Task ChapterSnapshotBuilder_WritesCastMapVariantsAndFrozenScriptVersionId()
    {
        var hero = _bible.AddEntry(BibleEntryCategory.Character, "GAARA", "Gaara", "Kazekage");
        var v1 = hero.AddRevision("v1", "{}", "hash-g1", authorId: _writerId, isInitialVersion: true, stageName: "Kazekage");
        v1.SubmitConcept(_writerId, "https://r2/g1.png", null, assetSha256: "sha256-gaara-modelsheet");
        v1.Lock(_editorId);
        _db.BibleEntries.Add(hero);
        _db.BibleEntryRevisions.Add(v1);

        var chapter = Chapter.Create(_series.Id, 1, "Chương 1").InProduction();
        _db.Chapters.Add(chapter);
        var page = Page.CreateNew(chapter.Id, 1, default, 1200, 1800, 300);
        _db.Pages.Add(page);
        var panel = Panel.Create(page.Id, 1, 1);
        _db.Panels.Add(panel);

        var pv = PanelBibleVariant.Create(panel.Id, hero.Id, v1.Id, _writerId);
        _db.PanelBibleVariants.Add(pv);

        var binding = ChapterBibleBinding.Create(chapter.Id, hero.Id, v1.Id, ChapterBibleBindingSource.Speaker, chosenByWriter: true);
        _db.ChapterBibleBindings.Add(binding);

        // Kịch bản đã chốt version
        var versionId = Guid.NewGuid();
        typeof(Chapter).GetProperty(nameof(Chapter.FrozenScriptVersionId))?.SetValue(chapter, versionId);
        await _db.SaveChangesAsync();

        // Setup multi-page version stream mock
        _pages.Setup(p => p.GetStreamVersionsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, long> { [page.Id] = 42L });

        var snapshot = await ChapterSnapshotBuilder.CreateAsync(_db, _pages.Object, chapter, _producerId, default);

        snapshot.Should().NotBeNull();
        snapshot.SnapshotNumber.Should().Be(1);

        var manifest = AiJson.Deserialize<SnapshotManifest>(snapshot.ManifestJson);
        manifest.Should().NotBeNull();
        manifest!.FrozenScriptVersionId.Should().Be(versionId);

        // Cast map
        manifest.Cast.Should().ContainSingle();
        var castItem = manifest.Cast[0];
        castItem.BibleEntryId.Should().Be(hero.Id);
        castItem.BoundRevisionId.Should().Be(v1.Id);
        castItem.ModelSheetSha256.Should().Be("sha256-gaara-modelsheet");

        // Panel variants
        manifest.PanelVariants.Should().ContainSingle();
        var variantItem = manifest.PanelVariants[0];
        variantItem.PanelId.Should().Be(panel.Id);
        variantItem.BibleEntryId.Should().Be(hero.Id);
        variantItem.RevisionId.Should().Be(v1.Id);

        // Multi-page stream version query
        manifest.Pages.Should().ContainSingle();
        manifest.Pages[0].CanvasStreamVersion.Should().Be(42L);
    }
}
