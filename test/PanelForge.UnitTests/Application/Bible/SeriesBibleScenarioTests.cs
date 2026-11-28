using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.Bible.Commands.AddBibleEntryRevision;
using PanelForge.Application.Features.Bible.Commands.CancelReplacementRevision;
using PanelForge.Application.Features.Bible.Commands.ConceptComments;
using PanelForge.Application.Features.Bible.Commands.ConfirmBibleEntryRevision;
using PanelForge.Application.Features.Bible.Commands.DefineBibleEntry;
using PanelForge.Application.Features.Bible.Commands.ReviewBibleEntryRevision;
using PanelForge.Application.Features.Bible.Commands.SetBibleEntryCrowd;
using PanelForge.Application.Features.Bible.Commands.SetChapterCastRevision;
using PanelForge.Application.Features.Bible.Commands.SubmitBibleConcept;
using PanelForge.Application.Features.Bible.Models;
using PanelForge.Application.Features.Chapters.Commands.FreezeScript;
using PanelForge.Application.Features.Chapters.Queries.GetChapterCast;
using PanelForge.Application.Features.Chapters.Services;
using PanelForge.Application.Features.ScriptLines.Commands.SaveChapterScript;
using PanelForge.Application.Features.ScriptLines.Models;
using PanelForge.Application.Features.ScriptVersions;
using PanelForge.Domain.Entities.Bible;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Entities.Workspaces;
using PanelForge.Domain.Enums;
using PanelForge.Infrastructure.Persistence;
using PanelForge.Infrastructure.Services;
using PanelForge.UnitTests.Common;
using Xunit;
using DomainSeries = PanelForge.Domain.Entities.Content.Series;
using MediatR;

namespace PanelForge.UnitTests.Application.Bible;

/// <summary>
/// End-to-end integration-style handler scenario tests for Series Bible (Q16-Q24):
/// - Speaker auto-match with name & auto-create stub (Dialogue + Thought)
/// - Highlight restrictions (only on Action/Caption/Sfx; reject Dialogue)
/// - Crowd flag synchronization
/// - Define draft entry promoting stub
/// - Concept versioning, Producer request-rework, threaded comments, and Editor confirm = lock
/// - Chapter cast inheritance across Ch1-Ch3, per-panel variant stages
/// - Supersede and cancel replacement rollback
/// - Submit Gate and Gate 1 (Start Production) freeze rules
/// </summary>
public class SeriesBibleScenarioTests
{
    private readonly PanelForgeDbContext _db = InMemoryDb.Create();
    private readonly Mock<IWorkspaceAuthorizationService> _auth = new();
    private readonly Mock<IPageRepository> _pageRepo = new();
    private readonly Mock<ISender> _sender = new();

    private readonly ChapterCastInheritanceResolver _resolver;
    private readonly ChapterCastService _castService;
    private readonly ChapterCastGate _castGate;

    private readonly StudioWorkspace _studio;
    private readonly DomainSeries _series;
    private readonly SeriesBible _bible;

    private readonly Guid _writerId = Guid.NewGuid();
    private readonly Guid _artistId = Guid.NewGuid();
    private readonly Guid _editorId = Guid.NewGuid();
    private readonly Guid _producerId = Guid.NewGuid();

    public SeriesBibleScenarioTests()
    {
        _studio = StudioWorkspace.Create("Manga Production Studio", _producerId);
        _db.StudioWorkspaces.Add(_studio);

        _series = DomainSeries.Create(_studio.Id, "Ninja Chronicles", ReadingDirection.RightToLeft);
        _db.Series.Add(_series);

        _bible = SeriesBible.Create(_series.Id, _studio.Id, "Ninja Master Bible");
        _db.SeriesBibles.Add(_bible);

        _db.SaveChanges();

        _resolver = new ChapterCastInheritanceResolver(_db);
        _castService = new ChapterCastService(_db, _resolver);
        _castGate = new ChapterCastGate(_db, _castService, _resolver);

        // RBAC setup for roles
        _auth.Setup(a => a.GetMemberRoleAsync(_writerId, _studio.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(WorkspaceRole.Writer);
        _auth.Setup(a => a.GetSeriesMemberRoleAsync(_writerId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(WorkspaceRole.Writer);
        _auth.Setup(a => a.GetMemberRoleAsync(_artistId, _studio.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(WorkspaceRole.Artist);
        _auth.Setup(a => a.GetSeriesMemberRoleAsync(_artistId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(WorkspaceRole.Artist);
        _auth.Setup(a => a.GetMemberRoleAsync(_editorId, _studio.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(WorkspaceRole.Editor);
        _auth.Setup(a => a.GetSeriesMemberRoleAsync(_editorId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(WorkspaceRole.Editor);
        _auth.Setup(a => a.GetMemberRoleAsync(_producerId, _studio.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(WorkspaceRole.Producer);
        _auth.Setup(a => a.GetSeriesMemberRoleAsync(_producerId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(WorkspaceRole.Producer);
    }

    [Fact]
    public async Task Scenario_ScriptAuthoring_SpeakerAutoMatchAndCreate_And_HighlightRestrictions()
    {
        // 1. Setup existing Canon Character
        var naruto = _bible.AddEntry(BibleEntryCategory.Character, "CHR01", "Cửu Vĩ", "Hokage tương lai");
        _db.BibleEntries.Add(naruto);

        // Location entry for highlight reference
        var leafVillage = _bible.AddEntry(BibleEntryCategory.Location, "LOC01", "Làng Lá", "Ngôi làng ninja");
        _db.BibleEntries.Add(leafVillage);

        var chapter = Chapter.Create(_series.Id, 1, "Chương 1");
        _db.Chapters.Add(chapter);
        var scene = Scene.Create(chapter.Id, 1, "Cảnh mở đầu");
        _db.Scenes.Add(scene);
        await _db.SaveChangesAsync();

        var saveHandler = new SaveChapterScriptCommandHandler(_db, _auth.Object, _pageRepo.Object, _castService, new PassThroughUnitOfWork());

        // 2. Validate highlight restriction: Passing refs on a Dialogue line MUST fail with 400 Validation
        var invalidBody = new ChapterScriptBody(
            ExpectedRevisionId: null,
            Pages:
            [
                new ScriptTreePage(
                    Id: null,
                    Number: 1,
                    Panels:
                    [
                        new ScriptTreePanel(
                            Id: null,
                            Number: 1,
                            Direction: "Toàn cảnh.",
                            CastRevisions: null,
                            Lines:
                            [
                                new ScriptTreeLine(null, ScriptLineKind.Dialogue, "Naruto", "Chào mừng tới Làng Lá!", [new ScriptLineRefInput("Làng Lá", leafVillage.Id)])
                            ])
                    ])
            ]);

        var invalidCmd = new SaveChapterScriptCommand(chapter.Id, _writerId, invalidBody);
        var invalidResult = await saveHandler.Handle(invalidCmd, CancellationToken.None);
        invalidResult.IsSuccess.Should().BeFalse();
        invalidResult.ErrorCode.Should().Be(ResultErrorCodes.Validation);
        invalidResult.ErrorMessage.Should().Contain("Chỉ cho phép gắn tham chiếu Series Bible trên dòng Action, Narration hoặc Sfx");

        // 3. Save valid lines:
        // - Dialogue with normalized name "cửu vĩ" -> speaker auto-match
        // - Dialogue with unknown "Sasuke" -> speaker auto-create stub
        // - Thought with unknown "Kakashi (nghĩ)" -> speaker auto-create stub stripped of "(nghĩ)"
        // - Action, Narration, Sfx with highlight refs -> valid!
        var validBody = new ChapterScriptBody(
            ExpectedRevisionId: null,
            Pages:
            [
                new ScriptTreePage(
                    Id: null,
                    Number: 1,
                    Panels:
                    [
                        new ScriptTreePanel(
                            Id: null,
                            Number: 1,
                            Direction: "Toàn cảnh.",
                            CastRevisions: null,
                            Lines:
                            [
                                new ScriptTreeLine(null, ScriptLineKind.Dialogue, "cửu vĩ", "Tôi sẽ không bao giờ bỏ cuộc!"),
                                new ScriptTreeLine(null, ScriptLineKind.Dialogue, "Sasuke", "Kẻ yếu đuối..."),
                                new ScriptTreeLine(null, ScriptLineKind.Thought, "Kakashi (nghĩ)", "Hai đứa này lại cãi nhau rồi."),
                                new ScriptTreeLine(null, ScriptLineKind.Action, null, "Mặt trời mọc trên Làng Lá bình yên.", [new ScriptLineRefInput("Làng Lá", leafVillage.Id)]),
                                new ScriptTreeLine(null, ScriptLineKind.Narration, null, "Năm ấy, Làng Lá bước vào kỷ nguyên mới.", [new ScriptLineRefInput("Làng Lá", leafVillage.Id)]),
                                new ScriptTreeLine(null, ScriptLineKind.Sfx, null, "BÙM!! Khói bao trùm cổng làng Làng Lá.", [new ScriptLineRefInput("Làng Lá", leafVillage.Id)])
                            ])
                    ])
            ]);

        var saveCmd = new SaveChapterScriptCommand(chapter.Id, _writerId, validBody);
        var saveResult = await saveHandler.Handle(saveCmd, CancellationToken.None);
        saveResult.IsSuccess.Should().BeTrue();

        // 4. Run cast recompute to verify speaker matching, stub creation, and highlight bindings
        await _castService.RecomputeAsync(chapter.Id, _writerId);
        await _db.SaveChangesAsync();

        var bindings = await _db.ChapterBibleBindings
            .Include(b => b.BibleEntry)
            .Where(b => b.ChapterId == chapter.Id)
            .ToListAsync();

        // Character matched via normalized name, not duplicated
        var narutoBinding = bindings.FirstOrDefault(b => b.BibleEntryId == naruto.Id);
        narutoBinding.Should().NotBeNull();
        narutoBinding!.Source.Should().Be(ChapterBibleBindingSource.Speaker);

        // Sasuke auto-created as stub
        var sasukeBinding = bindings.FirstOrDefault(b => b.BibleEntry.Name == "Sasuke");
        sasukeBinding.Should().NotBeNull();
        sasukeBinding!.BibleEntry.IsStub.Should().BeTrue();
        sasukeBinding.BibleEntry.Category.Should().Be(BibleEntryCategory.Character);
        sasukeBinding.Source.Should().Be(ChapterBibleBindingSource.Speaker);

        // Kakashi auto-created without "(nghĩ)" suffix
        var kakashiBinding = bindings.FirstOrDefault(b => b.BibleEntry.Name == "Kakashi");
        kakashiBinding.Should().NotBeNull();
        kakashiBinding!.BibleEntry.IsStub.Should().BeTrue();
        kakashiBinding.Source.Should().Be(ChapterBibleBindingSource.Speaker);

        // Làng Lá bound via Highlight
        var leafBinding = bindings.FirstOrDefault(b => b.BibleEntryId == leafVillage.Id);
        leafBinding.Should().NotBeNull();
        leafBinding!.Source.Should().Be(ChapterBibleBindingSource.Highlight);
    }

    [Fact]
    public async Task Scenario_StubLifecycle_CrowdFlag_DefineDraft_And_SubmitGate()
    {
        var chapter = Chapter.Create(_series.Id, 1, "Chương 1");
        _db.Chapters.Add(chapter);
        var scene = Scene.Create(chapter.Id, 1, "Cảnh phố");
        _db.Scenes.Add(scene);

        // Add script lines with speakers - RecomputeAsync will auto-create stub entries for them
        var line1 = ScriptLine.Create(scene.Id, 1, text: "Tránh đường!", kind: ScriptLineKind.Dialogue, speakerLabel: "Sasuke");
        var line2 = ScriptLine.Create(scene.Id, 2, text: "Cháy rồi!", kind: ScriptLineKind.Dialogue, speakerLabel: "Dân làng");
        _db.ScriptLines.AddRange(line1, line2);
        await _db.SaveChangesAsync();

        await _castService.RecomputeAsync(chapter.Id, _writerId);
        await _db.SaveChangesAsync();

        var sasukeStub = await _db.BibleEntries.FirstAsync(e => e.Name == "Sasuke");
        var crowdStub = await _db.BibleEntries.FirstAsync(e => e.Name == "Dân làng");

        // 1. Submit Gate should BLOCK because both entries are stubs and not crowd
        var initialGate = await _castGate.EvaluateSubmitAsync(chapter.Id);
        initialGate.CanSubmit.Should().BeFalse();
        initialGate.Blockers.Should().Contain(b => b.EntryName == "Sasuke");
        initialGate.Blockers.Should().Contain(b => b.EntryName == "Dân làng");

        // 2. Writer sets Crowd flag on "Dân làng"
        var crowdHandler = new SetBibleEntryCrowdCommandHandler(_db);
        var crowdResult = await crowdHandler.Handle(
            new SetBibleEntryCrowdCommand(_series.Id, crowdStub.Id, IsCrowd: true, _writerId),
            CancellationToken.None);
        crowdResult.IsSuccess.Should().BeTrue();

        var crowdBinding = await _db.ChapterBibleBindings.FirstAsync(b => b.ChapterId == chapter.Id && b.BibleEntryId == crowdStub.Id);
        crowdBinding.IsCrowdAtBind.Should().BeTrue();

        // Submit gate now ignores the crowd stub, but still blocks on "Sasuke"
        var partialGate = await _castGate.EvaluateSubmitAsync(chapter.Id);
        partialGate.CanSubmit.Should().BeFalse();
        partialGate.Blockers.Should().NotContain(b => b.EntryName == "Dân làng");
        partialGate.Blockers.Should().Contain(b => b.EntryName == "Sasuke");

        // 3. Writer defines "Sasuke" draft entry (DefineBibleEntryCommand)
        var defineHandler = new DefineBibleEntryCommandHandler(_db);
        var defineCmd = new DefineBibleEntryCommand(
            SeriesId: _series.Id,
            BibleEntryId: sasukeStub.Id,
            UserId: _writerId,
            Name: "Sasuke",
            Description: "Hậu duệ cuối cùng của gia tộc Uchiha"
        );

        var defineResult = await defineHandler.Handle(defineCmd, CancellationToken.None);
        defineResult.IsSuccess.Should().BeTrue();

        // Stub is promoted, v1 overwritten with Draft
        var reloadedSasuke = await _db.BibleEntries.Include(e => e.Revisions).FirstAsync(e => e.Id == sasukeStub.Id);
        reloadedSasuke.IsStub.Should().BeFalse();
        reloadedSasuke.Description.Should().Be("Hậu duệ cuối cùng của gia tộc Uchiha");
        reloadedSasuke.Revisions.Should().HaveCount(1);
        reloadedSasuke.Revisions.First().ReviewStatus.Should().Be(BibleRevisionStatus.Draft);

        // 4. Submit Gate now passes
        var readyGate = await _castGate.EvaluateSubmitAsync(chapter.Id);
        readyGate.Blockers.Should().BeEmpty(string.Join("; ", readyGate.Blockers.Select(b => $"{b.EntryName}: {b.Reason}")));
        readyGate.CanSubmit.Should().BeTrue();

        // 5. Submit script for review via lifecycle handler
        var submitHandler = new ScriptLifecycleCommandHandler(_db, _auth.Object, _castGate, _castService);
        var submitResult = await submitHandler.Handle(
            new SubmitScriptCommand(chapter.Id, _writerId, "Kịch bản Chương 1 hoàn thiện"),
            CancellationToken.None);

        submitResult.IsSuccess.Should().BeTrue();
        chapter.ScriptStatus.Should().Be(ScriptStatus.InReview);
        chapter.LastSubmittedVersionId.Should().NotBeNull();
    }

    [Fact]
    public async Task Scenario_ConceptReview_ProducerRework_ArtistReply_And_EditorConfirmLocksCanon()
    {
        // Setup defined character in Draft status
        var entry = _bible.AddEntry(BibleEntryCategory.Character, "CHR02", "Uchiha Itachi", "Thiên tài tộc Uchiha");
        var rev = entry.AddRevision("v1", "{}", "hash-itachi-v1", isInitialVersion: true);
        _db.BibleEntries.Add(entry);
        _db.BibleEntryRevisions.Add(rev);
        await _db.SaveChangesAsync();

        var submitConceptHandler = new SubmitBibleConceptCommandHandler(_db, _sender.Object, Moq.Mock.Of<PanelForge.Application.Common.Interfaces.Services.IAssetStorage>());
        var reworkHandler = new RequestReworkBibleEntryRevisionCommandHandler(_db);
        var commentHandler = new CreateBibleConceptCommentCommandHandler(_db);
        var replyHandler = new ReplyBibleConceptCommentCommandHandler(_db);
        var confirmHandler = new ConfirmBibleEntryRevisionCommandHandler(_db, _resolver);

        // 1. Artist submits first concept version (v1)
        var submitV1Cmd = new SubmitBibleConceptCommand(
            _series.Id,
            entry.Id,
            _artistId,
            new SubmitConceptArtRequest(
                ReferenceImageUrl: "https://storage.panelforge.dev/concepts/itachi_v1.png",
                AssetSha256: "sha256-itachi-v1"
            )
        );
        var submitV1Result = await submitConceptHandler.Handle(submitV1Cmd, CancellationToken.None);
        submitV1Result.IsSuccess.Should().BeTrue();

        var revAfterV1 = await _db.BibleEntryRevisions.Include(r => r.Events).FirstAsync(r => r.Id == rev.Id);
        revAfterV1.ReviewStatus.Should().Be(BibleRevisionStatus.ConceptSubmitted);
        revAfterV1.Events.Should().ContainSingle(e => e.ConceptVersionNumber == 1);

        // 2. Producer requests rework with required feedback (Q22: Producer is authorized to request rework)
        var reworkCmd = new RequestReworkBibleEntryRevisionCommand(
            _series.Id,
            entry.Id,
            rev.Id,
            _producerId,
            "Cần chỉnh lại biểu cảm lạnh lùng hơn và thêm áo choàng Akatsuki."
        );
        var reworkResult = await reworkHandler.Handle(reworkCmd, CancellationToken.None);
        reworkResult.IsSuccess.Should().BeTrue();

        var revAfterRework = await _db.BibleEntryRevisions.FirstAsync(r => r.Id == rev.Id);
        revAfterRework.ReviewStatus.Should().Be(BibleRevisionStatus.ReworkRequired);

        // Auto-generated comment from rework request exists
        var rootComments = await _db.BibleConceptComments
            .Where(c => c.RevisionId == rev.Id && c.ParentCommentId == null)
            .ToListAsync();
        rootComments.Should().ContainSingle();
        var reworkComment = rootComments.First();
        reworkComment.AuthorId.Should().Be(_producerId);

        // 3. Artist replies to rework comment
        var artistReplyCmd = new ReplyBibleConceptCommentCommand(
            _series.Id,
            entry.Id,
            rev.Id,
            reworkComment.Id,
            _artistId,
            new ReplyConceptCommentRequest("Đã ghi nhận, em đang vẽ lại áo choàng Akatsuki.")
        );
        var replyResult = await replyHandler.Handle(artistReplyCmd, CancellationToken.None);
        replyResult.IsSuccess.Should().BeTrue();

        // 4. Artist submits concept version 2 (v2)
        var submitV2Cmd = new SubmitBibleConceptCommand(
            _series.Id,
            entry.Id,
            _artistId,
            new SubmitConceptArtRequest(
                ReferenceImageUrl: "https://storage.panelforge.dev/concepts/itachi_v2.png",
                AssetSha256: "sha256-itachi-v2"
            )
        );
        var submitV2Result = await submitConceptHandler.Handle(submitV2Cmd, CancellationToken.None);
        submitV2Result.IsSuccess.Should().BeTrue();

        var revAfterV2 = await _db.BibleEntryRevisions.Include(r => r.Events).FirstAsync(r => r.Id == rev.Id);
        revAfterV2.ReviewStatus.Should().Be(BibleRevisionStatus.ConceptSubmitted);
        revAfterV2.Events.Should().HaveCount(4); // Created + Submit v1 + RequestRework + Submit v2
        var v2Event = revAfterV2.Events.First(e => e.ConceptVersionNumber == 2);
        revAfterV2.CurrentConceptEventId.Should().Be(v2Event.Id);

        // 5. Writer adds feedback comment on v2
        var writerCommentCmd = new CreateBibleConceptCommentCommand(
            _series.Id,
            entry.Id,
            rev.Id,
            _writerId,
            new CreateConceptCommentRequest("Bản v2 thần thái rất đúng với tính cách nhân vật!")
        );
        var writerCommentResult = await commentHandler.Handle(writerCommentCmd, CancellationToken.None);
        writerCommentResult.IsSuccess.Should().BeTrue();

        // Artist replies to Writer
        await replyHandler.Handle(new ReplyBibleConceptCommentCommand(
            _series.Id, entry.Id, rev.Id, writerCommentResult.Value.Id, _artistId,
            new ReplyConceptCommentRequest("Cảm ơn anh Writer ạ!")), CancellationToken.None);

        // 6. Editor confirms and locks Canon (Q20: Editor confirm = lock)
        var confirmCmd = new ConfirmBibleEntryRevisionCommand(
            _series.Id,
            entry.Id,
            rev.Id,
            _editorId,
            ConceptEventId: v2Event.Id
        );
        var confirmResult = await confirmHandler.Handle(confirmCmd, CancellationToken.None);
        confirmResult.IsSuccess.Should().BeTrue();

        var finalRev = await _db.BibleEntryRevisions.FirstAsync(r => r.Id == rev.Id);
        finalRev.ReviewStatus.Should().Be(BibleRevisionStatus.CanonLocked);
        finalRev.IsLocked.Should().BeTrue();
    }

    [Fact]
    public async Task Scenario_ChapterProgression_InheritanceCh1ToCh3_PerPanelStage_Supersede_CancelReplacement_And_Gate1()
    {
        // 1. Setup Character with v1 CanonLocked
        var naruto = _bible.AddEntry(BibleEntryCategory.Character, "CHR03", "Naruto", "Ninja Làng Lá");
        var v1 = naruto.AddRevision("v1 Canon", "{}", "hash-naruto-v1", isInitialVersion: true, stageName: "Trẻ em");
        v1.SubmitConcept(_artistId, "https://storage/v1.png", "concept v1", "hash-v1");
        v1.ConfirmCanon(_editorId, v1.CurrentConceptEventId!.Value); // CanonLocked
        _db.BibleEntries.Add(naruto);
        _db.BibleEntryRevisions.Add(v1);

        // Flashback variant for per-panel test
        var kidVariant = naruto.AddRevision("v1 Lúc nhỏ", "{}", "hash-naruto-kid", isInitialVersion: false, stageName: "Hồi tưởng");
        kidVariant.SubmitConcept(_artistId, "https://storage/kid.png", "concept kid", "hash-kid");
        kidVariant.ConfirmCanon(_editorId, kidVariant.CurrentConceptEventId!.Value); // CanonLocked
        _db.BibleEntryRevisions.Add(kidVariant);

        // Setup 3 Chapters
        var ch1 = Chapter.Create(_series.Id, 1, "Chương 1");
        ch1.SubmitScript(_writerId, Guid.NewGuid());
        ch1.StartProduction(_producerId, "hash-ch1", null); // Frozen in production
        var ch2 = Chapter.Create(_series.Id, 2, "Chương 2");
        var ch3 = Chapter.Create(_series.Id, 3, "Chương 3");
        _db.Chapters.AddRange(ch1, ch2, ch3);

        // Ch1 binding (Frozen with v1)
        var bindCh1 = ChapterBibleBinding.Create(ch1.Id, naruto.Id, v1.Id, ChapterBibleBindingSource.Speaker, chosenByWriter: false, isCrowdAtBind: false, _writerId);
        _db.ChapterBibleBindings.Add(bindCh1);

        // Ch2 binding initially inherits v1 from Ch1
        var bindCh2 = ChapterBibleBinding.Create(ch2.Id, naruto.Id, v1.Id, ChapterBibleBindingSource.Speaker, chosenByWriter: false, isCrowdAtBind: false, _writerId);
        _db.ChapterBibleBindings.Add(bindCh2);

        // Ch3 binding initially inherits v1
        var bindCh3 = ChapterBibleBinding.Create(ch3.Id, naruto.Id, v1.Id, ChapterBibleBindingSource.Speaker, chosenByWriter: false, isCrowdAtBind: false, _writerId);
        _db.ChapterBibleBindings.Add(bindCh3);

        // Setup Ch2 scenes, pages and panels
        var ch2Scene = Scene.Create(ch2.Id, 1, "Cảnh Ch2");
        _db.Scenes.Add(ch2Scene);
        var ch2Page = Page.CreateNew(ch2.Id, 1, default, 1200, 1800, 300);
        _db.Pages.Add(ch2Page);
        var ch2Panel1 = Panel.Create(ch2Page.Id, 1, 1);
        var ch2Panel2 = Panel.Create(ch2Page.Id, 2, 2);
        _db.Panels.AddRange(ch2Panel1, ch2Panel2);

        // Script line in Ch2 referencing Naruto
        var lineCh2 = ScriptLine.Create(ch2Scene.Id, 1, text: "Tôi đã lớn rồi!", kind: ScriptLineKind.Dialogue, speakerLabel: "Naruto");
        lineCh2.AssignToPanel(ch2Panel1.Id);
        _db.ScriptLines.Add(lineCh2);

        // 2. Per-panel variant stage: Panel 2 represents a flashback and uses the "Kid" variant
        var panelVariant = PanelBibleVariant.Create(ch2Panel2.Id, naruto.Id, kidVariant.Id, _writerId);
        _db.PanelBibleVariants.Add(panelVariant);

        await _db.SaveChangesAsync();

        var addRevHandler = new AddBibleEntryRevisionCommandHandler(_db, Moq.Mock.Of<PanelForge.Application.Common.Interfaces.Services.IAssetStorage>());
        var cancelHandler = new CancelReplacementRevisionCommandHandler(_db);
        var confirmHandler = new ConfirmBibleEntryRevisionCommandHandler(_db, _resolver);
        var freezeHandler = new FreezeChapterScriptCommandHandler(_db, _castGate);

        // 3. Writer creates replacement revision v2 (supersedes v1) from Ch2
        var supersedeCmd = new AddBibleEntryRevisionCommand(
            SeriesId: _series.Id,
            BibleEntryId: naruto.Id,
            UserId: _writerId,
            Summary: "Tạo hình Naruto Trẻ em v2",
            Description: "Naruto cập nhật trang phục",
            SupersedesRevisionId: v1.Id,
            SupersedeReason: "Cập nhật chi tiết trang phục chuẩn",
            ChapterId: ch2.Id,
            StageName: "Trẻ em"
        );
        var supersedeResult = await addRevHandler.Handle(supersedeCmd, CancellationToken.None);
        supersedeResult.IsSuccess.Should().BeTrue();
        var v2Id = supersedeResult.Value.Id;

        // Verify Ch2 binding points to v2 with chosen_by_writer = true
        var ch2BindingAfterSupersede = await _db.ChapterBibleBindings.FirstAsync(b => b.ChapterId == ch2.Id && b.BibleEntryId == naruto.Id);
        ch2BindingAfterSupersede.BoundRevisionId.Should().Be(v2Id);
        ch2BindingAfterSupersede.ChosenByWriter.Should().BeTrue();

        // 4. Test Cancel Replacement: Writer cancels v2 before any review event
        var cancelResult = await cancelHandler.Handle(
            new CancelReplacementRevisionCommand(_series.Id, naruto.Id, v2Id, _writerId),
            CancellationToken.None);
        cancelResult.IsSuccess.Should().BeTrue();

        // Verify Ch2 binding rolled back to inherited v1
        var ch2BindingAfterCancel = await _db.ChapterBibleBindings.FirstAsync(b => b.ChapterId == ch2.Id && b.BibleEntryId == naruto.Id);
        ch2BindingAfterCancel.BoundRevisionId.Should().Be(v1.Id);
        ch2BindingAfterCancel.ChosenByWriter.Should().BeFalse();

        // 5. Re-create replacement revision v2 for Ch2
        var reSupersedeResult = await addRevHandler.Handle(supersedeCmd, CancellationToken.None);
        reSupersedeResult.IsSuccess.Should().BeTrue();
        var reV2Id = reSupersedeResult.Value.Id;

        // 6. Ch3 inheritance: Ch3 has chosen_by_writer = false.
        // Ch3 inherits from Ch2 (closest previous chapter with binding), which bound reV2 (Q23: kế thừa về phía sau từ chương gần nhất)
        await _resolver.ResolveInheritedAsync(_series.Id, 2);
        var ch3BindingBeforeLock = await _db.ChapterBibleBindings.FirstAsync(b => b.ChapterId == ch3.Id && b.BibleEntryId == naruto.Id);
        ch3BindingBeforeLock.BoundRevisionId.Should().Be(reV2Id);

        // Ch1 keeps v1 (Q23 / 8A: supersede không tự chuyển chương cũ)
        var ch1BindingBeforeLock = await _db.ChapterBibleBindings.FirstAsync(b => b.ChapterId == ch1.Id && b.BibleEntryId == naruto.Id);
        ch1BindingBeforeLock.BoundRevisionId.Should().Be(v1.Id);

        // 7. Gate 1 test on Ch2:
        // Set Ch2 to InReview so it can attempt StartProduction
        ch2.SubmitScript(_writerId, Guid.NewGuid());
        await _db.SaveChangesAsync();

        // Gate 1 should FAIL because reV2 is still Draft (not CanonLocked)
        var gateBeforeLock = await _castGate.EvaluateStartAsync(ch2.Id);
        gateBeforeLock.CanStart.Should().BeFalse();
        gateBeforeLock.Blockers.Should().Contain(b => b.RevisionId == reV2Id);

        var freezeBeforeLockResult = await freezeHandler.Handle(new FreezeChapterScriptCommand(ch2.Id, _producerId), CancellationToken.None);
        freezeBeforeLockResult.IsSuccess.Should().BeFalse();
        freezeBeforeLockResult.ErrorCode.Should().Be(ResultErrorCodes.Conflict);

        // 8. Artist submits concept and Editor confirms & locks reV2
        var reV2 = await _db.BibleEntryRevisions.FirstAsync(r => r.Id == reV2Id);
        reV2.SubmitConcept(_artistId, "https://storage/naruto_v2.png", "Bản vẽ v2", "sha256-v2");
        var conceptEventId = reV2.CurrentConceptEventId!.Value;
        await _db.SaveChangesAsync();

        var confirmV2Result = await confirmHandler.Handle(
            new ConfirmBibleEntryRevisionCommand(_series.Id, naruto.Id, reV2Id, _editorId, conceptEventId),
            CancellationToken.None);
        confirmV2Result.IsSuccess.Should().BeTrue();

        // Confirming reV2 marks v1 as Superseded, and updates inheritance for un-frozen Ch3!
        var reloadedV1 = await _db.BibleEntryRevisions.Include(r => r.Events).FirstAsync(r => r.Id == v1.Id);
        reloadedV1.Events.Should().Contain(e => e.Type == BibleRevisionEventType.Superseded);

        var ch3BindingAfterLock = await _db.ChapterBibleBindings.FirstAsync(b => b.ChapterId == ch3.Id && b.BibleEntryId == naruto.Id);
        ch3BindingAfterLock.BoundRevisionId.Should().Be(reV2Id); // Ch3 now inherits reV2!

        // Frozen Ch1 still keeps v1
        var ch1Binding = await _db.ChapterBibleBindings.FirstAsync(b => b.ChapterId == ch1.Id && b.BibleEntryId == naruto.Id);
        ch1Binding.BoundRevisionId.Should().Be(v1.Id);

        // 9. Gate 1 now PASSES on Ch2
        var gateAfterLock = await _castGate.EvaluateStartAsync(ch2.Id);
        gateAfterLock.CanStart.Should().BeTrue();
        gateAfterLock.Blockers.Should().BeEmpty();

        // Producer successfully executes FreezeChapterScriptCommand (Gate 1 / Start Production)
        var freezeResult = await freezeHandler.Handle(new FreezeChapterScriptCommand(ch2.Id, _producerId), CancellationToken.None);
        freezeResult.IsSuccess.Should().BeTrue();

        ch2.ScriptStatus.Should().Be(ScriptStatus.Frozen);
        ch2.Status.Should().Be(ChapterStatus.InProduction);

        var finalCh2Binding = await _db.ChapterBibleBindings.FirstAsync(b => b.ChapterId == ch2.Id && b.BibleEntryId == naruto.Id);
        finalCh2Binding.BoundRevisionId.Should().Be(reV2Id);
    }

    [Fact]
    public async Task Scenario_SpeakerAutoMatchAndCreate_DialogueAndThought_CreatesStubOnce()
    {
        var chapter = Chapter.Create(_series.Id, 101, "Chương Thoại");
        _db.Chapters.Add(chapter);
        var scene = Scene.Create(chapter.Id, 1, "Cảnh 1");
        _db.Scenes.Add(scene);

        var line1 = ScriptLine.Create(scene.Id, 1, text: "Tôi là Sakura!", kind: ScriptLineKind.Dialogue, speakerLabel: "Sakura");
        var line2 = ScriptLine.Create(scene.Id, 2, text: "Mình phải cố lên...", kind: ScriptLineKind.Thought, speakerLabel: "Sakura (nghĩ)");
        _db.ScriptLines.AddRange(line1, line2);
        await _db.SaveChangesAsync();

        await _castService.RecomputeAsync(chapter.Id, _writerId);
        await _db.SaveChangesAsync();

        // Sakura should be created as a single stub
        var sakuraStubs = await _db.BibleEntries.Where(e => e.Name == "Sakura").ToListAsync();
        sakuraStubs.Should().ContainSingle();
        sakuraStubs[0].IsStub.Should().BeTrue();

        var chBinding = await _db.ChapterBibleBindings.FirstOrDefaultAsync(b => b.ChapterId == chapter.Id && b.BibleEntryId == sakuraStubs[0].Id);
        chBinding.Should().NotBeNull();
        chBinding!.Source.Should().Be(ChapterBibleBindingSource.Speaker);
    }

    [Fact]
    public async Task Scenario_CommitChapter2_ResolvesInheritanceFromChapter1_ThroughRealHandler()
    {
        // Setup Canon character
        var kakashi = _bible.AddEntry(BibleEntryCategory.Character, "CHR10", "Kakashi", "Ninja Thượng Đẳng");
        var v1 = kakashi.AddRevision("v1", "{}", "hash-k1", isInitialVersion: true, stageName: "Mặc định");
        v1.SubmitConcept(_artistId, "https://storage/k1.png", "c1", "hk1");
        v1.ConfirmCanon(_editorId, v1.CurrentConceptEventId!.Value);
        _db.BibleEntries.Add(kakashi);
        _db.BibleEntryRevisions.Add(v1);

        // Ch1 with Kakashi bound to v1
        var ch1 = Chapter.Create(_series.Id, 201, "Chương 201");
        ch1.SubmitScript(_writerId, Guid.NewGuid());
        ch1.StartProduction(_producerId, "hash-ch1", null); // Frozen
        var bindCh1 = ChapterBibleBinding.Create(ch1.Id, kakashi.Id, v1.Id, ChapterBibleBindingSource.Speaker, chosenByWriter: false, isCrowdAtBind: false, _writerId);
        _db.Chapters.Add(ch1);
        _db.ChapterBibleBindings.Add(bindCh1);

        // Ch2 has a line with speaker "Kakashi"
        var ch2 = Chapter.Create(_series.Id, 202, "Chương 202");
        _db.Chapters.Add(ch2);
        var scene2 = Scene.Create(ch2.Id, 1, "Cảnh Ch2");
        _db.Scenes.Add(scene2);
        var lineCh2 = ScriptLine.Create(scene2.Id, 1, text: "Nhìn vào mắt ta!", kind: ScriptLineKind.Dialogue, speakerLabel: "Kakashi");
        _db.ScriptLines.Add(lineCh2);
        await _db.SaveChangesAsync();

        // Real invocation for committing script version on Ch2
        var commitResult = await ScriptSnapshotBuilder.CommitAsync(
            _db, ch2.Id, _writerId, "Writer commit ch2", CancellationToken.None, _castService);
        await _db.SaveChangesAsync();

        commitResult.Should().NotBeNull();

        // Chapter 2 binding should exist and inherit v1 from Chapter 1!
        var ch2Binding = await _db.ChapterBibleBindings.FirstOrDefaultAsync(b => b.ChapterId == ch2.Id && b.BibleEntryId == kakashi.Id);
        ch2Binding.Should().NotBeNull();
        ch2Binding!.BoundRevisionId.Should().Be(v1.Id);
        ch2Binding.ChosenByWriter.Should().BeFalse();
    }

    [Fact]
    public async Task Scenario_DecimalChapterInheritance_10_5_InheritsFrom_10_And_11_InheritsFrom_10_5()
    {
        var entry = _bible.AddEntry(BibleEntryCategory.Character, "CHR20", "Gai", "Mãnh thú Làng Lá");
        var v10 = entry.AddRevision("v10", "{}", "hash-g10", isInitialVersion: true, stageName: "Thường");
        v10.SubmitConcept(_artistId, "https://storage/g10.png", "c10", "hg10");
        v10.ConfirmCanon(_editorId, v10.CurrentConceptEventId!.Value);

        var v105 = entry.AddRevision("v10.5", "{}", "hash-g105", isInitialVersion: false, stageName: "Mở Bát Môn");
        v105.SubmitConcept(_artistId, "https://storage/g105.png", "c105", "hg105");
        v105.ConfirmCanon(_editorId, v105.CurrentConceptEventId!.Value);

        _db.BibleEntries.Add(entry);
        _db.BibleEntryRevisions.AddRange(v10, v105);

        // Chapter 10, 10.5, 11
        var ch10 = Chapter.Create(_series.Id, 10m, "Chương 10");
        var ch105 = Chapter.Create(_series.Id, 10.5m, "Chương 10.5 (Ngoại truyện)");
        var ch11 = Chapter.Create(_series.Id, 11m, "Chương 11");
        _db.Chapters.AddRange(ch10, ch105, ch11);

        // Ch10 is frozen with v10
        ch10.SubmitScript(_writerId, Guid.NewGuid());
        ch10.StartProduction(_producerId, "hash-10", null);
        var bind10 = ChapterBibleBinding.Create(ch10.Id, entry.Id, v10.Id, ChapterBibleBindingSource.Speaker, chosenByWriter: false, isCrowdAtBind: false, _writerId);
        _db.ChapterBibleBindings.Add(bind10);

        // Ch10.5 starts un-frozen, chosen_by_writer = false
        var bind105 = ChapterBibleBinding.Create(ch105.Id, entry.Id, null, ChapterBibleBindingSource.Speaker, chosenByWriter: false, isCrowdAtBind: false, _writerId);
        _db.ChapterBibleBindings.Add(bind105);

        // Ch11 starts un-frozen, chosen_by_writer = false
        var bind11 = ChapterBibleBinding.Create(ch11.Id, entry.Id, null, ChapterBibleBindingSource.Speaker, chosenByWriter: false, isCrowdAtBind: false, _writerId);
        _db.ChapterBibleBindings.Add(bind11);
        await _db.SaveChangesAsync();

        // 1. Resolve inherited for 10.5 -> inherits from 10 (v10)
        await _resolver.ResolveInheritedAsync(_series.Id, 10.5m);
        bind105.BoundRevisionId.Should().Be(v10.Id);

        // 2. Ch10.5 writer explicitly chooses v105 (Bát Môn)
        bind105.UpdateBinding(v105.Id, ChapterBibleBindingSource.Speaker, chosenByWriter: true, _writerId);
        await _db.SaveChangesAsync();

        // 3. Resolve inherited from 11 -> Ch11 inherits from closest previous chapter (10.5), getting v105!
        await _resolver.ResolveInheritedAsync(_series.Id, 11m);
        bind11.BoundRevisionId.Should().Be(v105.Id);
        await _db.SaveChangesAsync();

        // 4. GET /cast on Chapter 10.5 returns decimal ChapterNumber and decimal InheritedFromChapterNumber
        var queryHandler = new GetChapterCastQueryHandler(_db, _auth.Object, _castGate);
        var cast105Result = await queryHandler.Handle(new GetChapterCastQuery(ch105.Id, _writerId), default);
        cast105Result.IsSuccess.Should().BeTrue();
        cast105Result.Value!.ChapterNumber.Should().Be(10.5m);

        var cast11Result = await queryHandler.Handle(new GetChapterCastQuery(ch11.Id, _writerId), default);
        cast11Result.IsSuccess.Should().BeTrue();
        cast11Result.Value!.ChapterNumber.Should().Be(11m);
        var gaiIn11 = cast11Result.Value.Entities.First(e => e.BibleEntryId == entry.Id);
        gaiIn11.InheritedFromChapterNumber.Should().Be(10.5m);
    }

    [Fact]
    public async Task Scenario_PerPanelVariant_Flashback_Gate1Validation()
    {
        var entry = _bible.AddEntry(BibleEntryCategory.Character, "CHR30", "Sasuke", "Đối thủ");
        var canonRev = entry.AddRevision("v1", "{}", "h-s1", isInitialVersion: true, stageName: "Mặc định");
        canonRev.SubmitConcept(_artistId, "https://storage/s1.png", "c-s1", "hs1");
        canonRev.ConfirmCanon(_editorId, canonRev.CurrentConceptEventId!.Value);

        var flashbackRev = entry.AddRevision("v1-kid", "{}", "h-kid", isInitialVersion: false, stageName: "Lúc nhỏ");
        // Flashback is Draft (not yet CanonLocked)
        _db.BibleEntries.Add(entry);
        _db.BibleEntryRevisions.AddRange(canonRev, flashbackRev);

        var chapter = Chapter.Create(_series.Id, 301, "Chương Hồi Tưởng");
        _db.Chapters.Add(chapter);
        var page = Page.CreateNew(chapter.Id, 1, default, 1200, 1800, 300);
        _db.Pages.Add(page);
        var panel1 = Panel.Create(page.Id, 1, 1);
        var panel2 = Panel.Create(page.Id, 2, 2);
        _db.Panels.AddRange(panel1, panel2);

        // Chapter uses canonRev
        var binding = ChapterBibleBinding.Create(chapter.Id, entry.Id, canonRev.Id, ChapterBibleBindingSource.Speaker, chosenByWriter: false, isCrowdAtBind: false, _writerId);
        _db.ChapterBibleBindings.Add(binding);

        // Panel 2 has flashback variant (not locked)
        var panelVariant = PanelBibleVariant.Create(panel2.Id, entry.Id, flashbackRev.Id, _writerId);
        _db.PanelBibleVariants.Add(panelVariant);

        chapter.SubmitScript(_writerId, Guid.NewGuid());
        await _db.SaveChangesAsync();

        // Gate 1 should fail because panel 2 flashback variant is not CanonLocked
        var gateBeforeLock = await _castGate.EvaluateStartAsync(chapter.Id);
        gateBeforeLock.CanStart.Should().BeFalse();
        gateBeforeLock.Blockers.Should().Contain(b => b.RevisionId == flashbackRev.Id && b.SourceExplanation!.Contains("Panel 1.2"));

        // Now lock flashbackRev
        flashbackRev.SubmitConcept(_artistId, "https://storage/kid.png", "concept kid", "hkid");
        flashbackRev.ConfirmCanon(_editorId, flashbackRev.CurrentConceptEventId!.Value);
        await _db.SaveChangesAsync();

        // Gate 1 should now pass
        var gateAfterLock = await _castGate.EvaluateStartAsync(chapter.Id);
        gateAfterLock.CanStart.Should().BeTrue();
        gateAfterLock.Blockers.Should().BeEmpty();
    }

    [Fact]
    public async Task Scenario_EditorConfirmLocksCanon_RejectsWriterOrArtist()
    {
        var entry = _bible.AddEntry(BibleEntryCategory.Prop, "PRP01", "Kiếm Kusanagi", "Bảo kiếm");
        var rev = entry.AddRevision("v1", "{}", "h-prop", isInitialVersion: true);
        rev.SubmitConcept(_artistId, "https://storage/sword.png", "Kiếm bản 1", "h-sw");
        _db.BibleEntries.Add(entry);
        _db.BibleEntryRevisions.Add(rev);
        await _db.SaveChangesAsync();

        var confirmHandler = new ConfirmBibleEntryRevisionCommandHandler(_db, _resolver);
        var conceptEventId = rev.CurrentConceptEventId!.Value;

        // Artist cannot confirm (BR-06) -> 403 Forbidden
        var artistResult = await confirmHandler.Handle(
            new ConfirmBibleEntryRevisionCommand(_series.Id, entry.Id, rev.Id, _artistId, conceptEventId),
            CancellationToken.None);
        artistResult.IsSuccess.Should().BeFalse();
        artistResult.ErrorCode.Should().Be(ResultErrorCodes.Forbidden);

        // Editor confirms -> Success & CanonLocked
        var editorResult = await confirmHandler.Handle(
            new ConfirmBibleEntryRevisionCommand(_series.Id, entry.Id, rev.Id, _editorId, conceptEventId),
            CancellationToken.None);
        editorResult.IsSuccess.Should().BeTrue();
        rev.IsLocked.Should().BeTrue();
        rev.ReviewStatus.Should().Be(BibleRevisionStatus.CanonLocked);
    }

    [Fact]
    public async Task Scenario_ProducerRequestRework_TransitionsToReworkRequired()
    {
        var entry = _bible.AddEntry(BibleEntryCategory.Character, "CHR40", "Jiraiya", "Hiền nhân Cóc");
        var rev = entry.AddRevision("v1", "{}", "h-j1", isInitialVersion: true);
        rev.SubmitConcept(_artistId, "https://storage/j1.png", "Jiraiya v1", "hj1");
        _db.BibleEntries.Add(entry);
        _db.BibleEntryRevisions.Add(rev);
        await _db.SaveChangesAsync();

        var reworkHandler = new RequestReworkBibleEntryRevisionCommandHandler(_db);
        var result = await reworkHandler.Handle(
            new RequestReworkBibleEntryRevisionCommand(_series.Id, entry.Id, rev.Id, _producerId, "Vẽ mặt chưa đủ già"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        rev.ReviewStatus.Should().Be(BibleRevisionStatus.ReworkRequired);
    }

    [Fact]
    public async Task Scenario_ConceptVersion_CommentsAndReplies_Threaded()
    {
        var entry = _bible.AddEntry(BibleEntryCategory.Character, "CHR50", "Tsunade", "Hokage Đệ Ngũ");
        var rev = entry.AddRevision("v1", "{}", "h-tsu", isInitialVersion: true);
        rev.SubmitConcept(_artistId, "https://storage/tsu.png", "Bản vẽ Tsunade", "htsu");
        _db.BibleEntries.Add(entry);
        _db.BibleEntryRevisions.Add(rev);
        await _db.SaveChangesAsync();

        // Writer posts a comment
        var commentHandler = new CreateBibleConceptCommentCommandHandler(_db);
        var commentResult = await commentHandler.Handle(
            new CreateBibleConceptCommentCommand(_series.Id, entry.Id, rev.Id, _writerId, new CreateConceptCommentRequest("Màu áo cần xanh lá đậm hơn")),
            CancellationToken.None);
        commentResult.IsSuccess.Should().BeTrue();
        var parentCommentId = commentResult.Value.Id;

        // Artist replies to the comment
        var replyHandler = new ReplyBibleConceptCommentCommandHandler(_db);
        var replyResult = await replyHandler.Handle(
            new ReplyBibleConceptCommentCommand(_series.Id, entry.Id, rev.Id, parentCommentId, _artistId, new ReplyConceptCommentRequest("Đã ghi nhận, sẽ sửa lại trong concept tiếp theo")),
            CancellationToken.None);
        replyResult.IsSuccess.Should().BeTrue();
        replyResult.Value.ParentCommentId.Should().Be(parentCommentId);

        // Verify threaded comments in DB
        var comments = await _db.BibleConceptComments.Where(c => c.RevisionId == rev.Id).ToListAsync();
        comments.Should().HaveCount(2);
    }
}
