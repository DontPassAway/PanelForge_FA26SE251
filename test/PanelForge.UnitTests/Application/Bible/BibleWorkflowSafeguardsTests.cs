using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Moq;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.Bible.Commands.AddBibleEntryRevision;
using PanelForge.Application.Features.Bible.Commands.CancelReplacementRevision;
using PanelForge.Application.Features.Bible.Commands.ConfirmBibleEntryRevision;
using PanelForge.Application.Features.Bible.Commands.EndorseBibleRevision;
using PanelForge.Application.Features.Bible.Commands.ReviewBibleEntryRevision;
using PanelForge.Application.Features.Bible.Commands.SubmitBibleConcept;
using PanelForge.Application.Features.Bible.Models;
using PanelForge.Application.Features.Bible.Queries.GetBibleDesignQueue;
using PanelForge.Application.Features.Chapters.Commands.FreezeScript;
using PanelForge.Application.Features.Chapters.Queries.GetChapterCast;
using PanelForge.Application.Features.Chapters.Services;
using PanelForge.Application.Features.ScriptVersions;
using PanelForge.Domain.Entities.Bible;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Entities.Identity;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Entities.Workspaces;
using PanelForge.Domain.Enums;
using PanelForge.Infrastructure.Persistence;
using PanelForge.Infrastructure.Services;
using PanelForge.UnitTests.Common;
using Xunit;

namespace PanelForge.UnitTests.Application.Bible;

public class BibleWorkflowSafeguardsTests
{
    private static (PanelForgeDbContext Db, Guid SeriesId, Series Series, SeriesBible Bible) CreateContextWithSeries(
        bool allowSoloCreatorReview = false)
    {
        var db = InMemoryDb.Create();
        var owner = User.Create("owner@panelforge.local", "Owner");
        var workspace = StudioWorkspace.Create("Studio Alpha", owner.Id);
        var series = Series.Create(workspace.Id, "Test Series", ReadingDirection.RightToLeft);
        if (allowSoloCreatorReview)
        {
            series.SetAllowSoloCreatorReview(true);
        }

        var bible = SeriesBible.Create(series.Id, workspace.Id, "Series Bible");
        db.Users.Add(owner);
        db.StudioWorkspaces.Add(workspace);
        db.Series.Add(series);
        db.SeriesBibles.Add(bible);
        db.SaveChanges();

        return (db, series.Id, series, bible);
    }

    [Fact]
    public async Task SoftWriterEndorsement_TogglesAndResetsOnNewConceptUploadAndRework()
    {
        var (db, seriesId, _, bible) = CreateContextWithSeries();
        var writer = User.Create("writer@panelforge.local", "Writer Minh");
        var artistId = Guid.NewGuid();
        db.Users.Add(writer);

        var entry = bible.AddEntry(BibleEntryCategory.Character, "CHR-KAI", "Kaito", "Protagonist");
        var rev = entry.AddRevision("v1", "{}", "hash-1", authorId: writer.Id, isInitialVersion: true);
        rev.SubmitConcept(artistId, "https://assets.local/kaito-v1.png", null);
        db.BibleEntries.Add(entry);
        db.BibleEntryRevisions.Add(rev);
        await db.SaveChangesAsync();

        var endorseHandler = new EndorseBibleRevisionCommandHandler(db);

        var endorsed = await endorseHandler.Handle(
            new EndorseBibleRevisionCommand(seriesId, rev.Id, writer.Id, true),
            CancellationToken.None);
        endorsed.IsSuccess.Should().BeTrue();
        endorsed.Value!.WriterEndorsement.Should().NotBeNull();
        endorsed.Value.WriterEndorsement!.Endorsed.Should().BeTrue();
        endorsed.Value.WriterEndorsement.EndorsedByName.Should().Be("Writer Minh");
        endorsed.Value.WriterEndorsement.ConceptVersion.Should().Be(1);

        // Toggle off explicitly
        var unendorsed = await endorseHandler.Handle(
            new EndorseBibleRevisionCommand(seriesId, rev.Id, writer.Id, false),
            CancellationToken.None);
        unendorsed.IsSuccess.Should().BeTrue();
        unendorsed.Value!.WriterEndorsement!.Endorsed.Should().BeFalse();

        // Re-endorse idempotently, then Artist uploads v2 concept -> clears previous endorsement
        await endorseHandler.Handle(new EndorseBibleRevisionCommand(seriesId, rev.Id, writer.Id, true), CancellationToken.None);
        rev.WriterEndorsedAt.Should().NotBeNull();

        var senderMock = new Mock<ISender>();
        var submitConceptHandler = new SubmitBibleConceptCommandHandler(db, senderMock.Object);
        var resubmit = await submitConceptHandler.Handle(
            new SubmitBibleConceptCommand(seriesId, entry.Id, artistId,
                new SubmitConceptArtRequest(ReferenceImageUrl: "https://assets.local/kaito-v2.png")),
            CancellationToken.None);
        resubmit.IsSuccess.Should().BeTrue();
        rev.WriterEndorsedAt.Should().BeNull();
        rev.WriterEndorsedByUserId.Should().BeNull();
        rev.WriterEndorsedConceptVersion.Should().BeNull();
        resubmit.Value!.WriterEndorsement!.Endorsed.Should().BeFalse();

        // Re-endorse v2 then RequestRework -> also clears endorsement
        await endorseHandler.Handle(new EndorseBibleRevisionCommand(seriesId, rev.Id, writer.Id, true), CancellationToken.None);
        rev.WriterEndorsedAt.Should().NotBeNull();
        rev.WriterEndorsedConceptVersion.Should().Be(2);

        var reworkHandler = new RequestReworkBibleEntryRevisionCommandHandler(db);
        var rework = await reworkHandler.Handle(
            new RequestReworkBibleEntryRevisionCommand(seriesId, entry.Id, rev.Id, Guid.NewGuid(), "Fix shoulder silhouette"),
            CancellationToken.None);
        rework.IsSuccess.Should().BeTrue();
        rev.ReviewStatus.Should().Be(BibleRevisionStatus.ReworkRequired);
        rev.WriterEndorsedAt.Should().BeNull();
    }

    [Fact]
    public async Task DesignQueue_PrioritizesReworkRequiredAboveDraftAndIncludesBoth()
    {
        var (db, seriesId, _, bible) = CreateContextWithSeries();
        var userId = Guid.NewGuid();

        var draftEntry = bible.AddEntry(BibleEntryCategory.Character, "CHR-01", "Alpha", "Draft item");
        var draftRev = draftEntry.AddRevision("v1", "{}", "h1", authorId: userId, isInitialVersion: true);

        var reworkEntry = bible.AddEntry(BibleEntryCategory.Character, "CHR-02", "Beta", "Returned item");
        var reworkRev = reworkEntry.AddRevision("v1", "{}", "h2", authorId: userId, isInitialVersion: true);
        reworkRev.SubmitConcept(userId, "https://assets.local/beta.png", null);
        reworkRev.RequestRework(Guid.NewGuid(), "Redraw helmet");

        db.BibleEntries.AddRange(draftEntry, reworkEntry);
        db.BibleEntryRevisions.AddRange(draftRev, reworkRev);
        await db.SaveChangesAsync();

        var handler = new GetBibleDesignQueueQueryHandler(db);
        var result = await handler.Handle(new GetBibleDesignQueueQuery(seriesId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
        result.Value![0].EntryId.Should().Be(reworkEntry.Id);
        result.Value[0].ReviewStatus.Should().Be(BibleRevisionStatus.ReworkRequired);
        result.Value[0].IsPriorityRework.Should().BeTrue();
        result.Value[1].EntryId.Should().Be(draftEntry.Id);
        result.Value[1].ReviewStatus.Should().Be(BibleRevisionStatus.Draft);
        result.Value[1].IsPriorityRework.Should().BeFalse();
    }

    [Fact]
    public async Task ConfirmCanon_AllowSoloCreatorReview_Enforces403WhenFalse_AndLogsSelfApprovedWhenTrue()
    {
        var sharedUserId = Guid.NewGuid();
        var inheritance = new Mock<IChapterCastInheritanceResolver>();

        // 1. Strict 4-eyes mode (AllowSoloCreatorReview = false)
        var (dbStrict, seriesStrictId, _, bibleStrict) = CreateContextWithSeries(allowSoloCreatorReview: false);
        var entryStrict = bibleStrict.AddEntry(BibleEntryCategory.Character, "CHR-S", "SoloStrict", "Desc");
        var revStrict = entryStrict.AddRevision("v1", "{}", "hs", authorId: sharedUserId, isInitialVersion: true);
        revStrict.SubmitConcept(sharedUserId, "https://assets.local/s1.png", null);
        dbStrict.BibleEntries.Add(entryStrict);
        dbStrict.BibleEntryRevisions.Add(revStrict);
        await dbStrict.SaveChangesAsync();

        var strictHandler = new ConfirmBibleEntryRevisionCommandHandler(dbStrict, inheritance.Object);
        var strictResult = await strictHandler.Handle(
            new ConfirmBibleEntryRevisionCommand(seriesStrictId, entryStrict.Id, revStrict.Id, sharedUserId),
            CancellationToken.None);

        strictResult.IsSuccess.Should().BeFalse();
        strictResult.ErrorCode.Should().Be(ResultErrorCodes.Forbidden);
        revStrict.IsLocked.Should().BeFalse();

        // 2. Solo creator escape hatch enabled (AllowSoloCreatorReview = true)
        var (dbSolo, seriesSoloId, _, bibleSolo) = CreateContextWithSeries(allowSoloCreatorReview: true);
        var entrySolo = bibleSolo.AddEntry(BibleEntryCategory.Character, "CHR-A", "SoloAllowed", "Desc");
        var revSolo = entrySolo.AddRevision("v1", "{}", "ha", authorId: sharedUserId, isInitialVersion: true);
        revSolo.SubmitConcept(sharedUserId, "https://assets.local/s2.png", null);
        dbSolo.BibleEntries.Add(entrySolo);
        dbSolo.BibleEntryRevisions.Add(revSolo);
        await dbSolo.SaveChangesAsync();

        var soloHandler = new ConfirmBibleEntryRevisionCommandHandler(dbSolo, inheritance.Object);
        var soloResult = await soloHandler.Handle(
            new ConfirmBibleEntryRevisionCommand(seriesSoloId, entrySolo.Id, revSolo.Id, sharedUserId),
            CancellationToken.None);

        soloResult.IsSuccess.Should().BeTrue();
        revSolo.IsLocked.Should().BeTrue();
        revSolo.ReviewStatus.Should().Be(BibleRevisionStatus.CanonLocked);

        var audit = await dbSolo.AuditLogs.FirstOrDefaultAsync(a => a.Action == "CANON_LOCK_SELF_APPROVED");
        audit.Should().NotBeNull();
        audit!.Details.Should().Contain("\"self-approved\":true");
    }

    [Fact]
    public async Task CancelReplacementRevision_CancelsUnlockedDraftSupersede_AndRejectsNonSupersedeOrLocked()
    {
        var (db, seriesId, _, bible) = CreateContextWithSeries();
        var userId = Guid.NewGuid();
        var entry = bible.AddEntry(BibleEntryCategory.Character, "CHR-CAN", "Rei", "Original");
        var v1Locked = entry.AddRevision("v1", "{}", "h1", authorId: userId, isInitialVersion: true);
        v1Locked.SubmitConcept(Guid.NewGuid(), "https://assets.local/v1.png", null);
        v1Locked.Lock(userId);

        var v2SupersedeLocked = entry.AddRevision("v2 locked", "{}", "h2l", authorId: userId, supersedesRevisionId: v1Locked.Id);
        v2SupersedeLocked.SubmitConcept(Guid.NewGuid(), "https://assets.local/v2.png", null);
        v2SupersedeLocked.Lock(userId);

        var v3SupersedeDraft = entry.AddRevision("v3 draft", "{}", "h3", authorId: userId, supersedesRevisionId: v2SupersedeLocked.Id);
        db.BibleEntries.Add(entry);
        db.BibleEntryRevisions.AddRange(v1Locked, v2SupersedeLocked, v3SupersedeDraft);
        await db.SaveChangesAsync();

        var handler = new CancelReplacementRevisionCommandHandler(db);

        // Cannot cancel non-supersede revision
        var nonSupersedeFail = await handler.Handle(
            new CancelReplacementRevisionCommand(seriesId, null, v1Locked.Id, userId),
            CancellationToken.None);
        nonSupersedeFail.IsSuccess.Should().BeFalse();
        nonSupersedeFail.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        nonSupersedeFail.ErrorMessage.Should().Contain("supersedes_revision_id");

        // Cannot cancel locked supersede revision
        var lockedFail = await handler.Handle(
            new CancelReplacementRevisionCommand(seriesId, null, v2SupersedeLocked.Id, userId),
            CancellationToken.None);
        lockedFail.IsSuccess.Should().BeFalse();
        lockedFail.ErrorCode.Should().Be(ResultErrorCodes.Conflict);

        // Cancel unlocked supersede draft successfully
        var canceled = await handler.Handle(
            new CancelReplacementRevisionCommand(seriesId, null, v3SupersedeDraft.Id, userId),
            CancellationToken.None);
        canceled.IsSuccess.Should().BeTrue();
        v3SupersedeDraft.IsDeleted.Should().BeTrue();
        v2SupersedeLocked.ReviewStatus.Should().Be(BibleRevisionStatus.CanonLocked);
    }

    [Fact]
    public async Task InheritanceResolver_OrdersByDecimalChapterNumber_AndStopsSupersedeChainAtDraft()
    {
        var (db, seriesId, _, bible) = CreateContextWithSeries();
        var artistId = Guid.NewGuid();
        var editorId = Guid.NewGuid();
        var entry = bible.AddEntry(BibleEntryCategory.Character, "CHR-DEC", "Hana", "Heroine");

        var v1 = entry.AddRevision("v1", "{}", "h1", authorId: artistId, isInitialVersion: true);
        v1.SubmitConcept(artistId, "https://assets.local/1.png", null);
        v1.Lock(editorId);

        var v2 = entry.AddRevision("v2", "{}", "h2", authorId: artistId, supersedesRevisionId: v1.Id);
        v2.SubmitConcept(artistId, "https://assets.local/2.png", null);
        v2.Lock(editorId);
        v1.MarkSuperseded(editorId, v2.VersionNumber, "Replaced by v2");

        var v3Draft = entry.AddRevision("v3 draft", "{}", "h3", authorId: artistId, supersedesRevisionId: v2.Id);

        var ch10 = Chapter.Create(seriesId, 1.0m, "Ch 1");
        var ch15 = Chapter.Create(seriesId, 1.5m, "Ch 1.5 Interlude");
        var ch20 = Chapter.Create(seriesId, 2.0m, "Ch 2");
        db.BibleEntries.Add(entry);
        db.BibleEntryRevisions.AddRange(v1, v2, v3Draft);
        db.Chapters.AddRange(ch20, ch10, ch15);

        var b10 = ChapterBibleBinding.Create(ch10.Id, entry.Id, v1.Id, ChapterBibleBindingSource.Speaker, chosenByWriter: true);
        var b15 = ChapterBibleBinding.Create(ch15.Id, entry.Id, null, ChapterBibleBindingSource.Speaker, chosenByWriter: false);
        var b20 = ChapterBibleBinding.Create(ch20.Id, entry.Id, null, ChapterBibleBindingSource.Speaker, chosenByWriter: false);
        db.ChapterBibleBindings.AddRange(b20, b10, b15);
        await db.SaveChangesAsync();

        var resolver = new ChapterCastInheritanceResolver(db);
        await resolver.ResolveInheritedAsync(seriesId, 1.0m);

        b15.BoundRevisionId.Should().Be(v2.Id);
        b20.BoundRevisionId.Should().Be(v2.Id);
        b20.BoundRevisionId.Should().NotBe(v3Draft.Id);
    }

    [Fact]
    public async Task SpeakerAutoLinksByName_AndDraftRenameAllowedBeforeLockOnly()
    {
        var (db, seriesId, _, bible) = CreateContextWithSeries();
        var writerId = Guid.NewGuid();
        var entry = bible.AddEntry(BibleEntryCategory.Character, "Rin Tachibana", "Detective");
        var v1 = entry.AddRevision("v1", "{}", "h1", authorId: writerId, isInitialVersion: true);
        db.BibleEntries.Add(entry);
        db.BibleEntryRevisions.Add(v1);

        var chapter = Chapter.Create(seriesId, 1m, "Ch1");
        var scene = Scene.CreateDefault(chapter.Id);
        var line = ScriptLine.Create(scene.Id, 1, dialogueText: "Follow me!", speakerLabel: "Rin Tachibana");
        db.Chapters.Add(chapter);
        db.Scenes.Add(scene);
        db.ScriptLines.Add(line);
        await db.SaveChangesAsync();

        // Rename typo before lock succeeds
        var addRevHandler = new AddBibleEntryRevisionCommandHandler(db);
        var renameRes = await addRevHandler.Handle(
            new AddBibleEntryRevisionCommand(
                SeriesId: seriesId,
                BibleEntryId: entry.Id,
                UserId: writerId,
                Summary: "Fix spelling",
                Name: "Rin Tachibane"),
            CancellationToken.None);
        renameRes.IsSuccess.Should().BeTrue();
        entry.Name.Should().Be("Rin Tachibane");

        // Auto-link speaker via name instead of creating a duplicate stub
        line.UpdateContent("Follow me!", null, speakerLabel: "Rin Tachibane");
        await db.SaveChangesAsync();

        var inheritance = new ChapterCastInheritanceResolver(db);
        var castService = new ChapterCastService(db, inheritance);
        await castService.RecomputeAsync(chapter.Id, writerId);
        await db.SaveChangesAsync();

        var bindings = await db.ChapterBibleBindings.Where(b => b.ChapterId == chapter.Id).ToListAsync();
        bindings.Should().ContainSingle(b => b.BibleEntryId == entry.Id);
        var totalEntries = await db.BibleEntries.CountAsync();
        totalEntries.Should().Be(1);

        // Lock latest revision -> subsequent rename attempt fails validation
        var v2 = entry.Revisions.OrderByDescending(r => r.VersionNumber).First();
        v2.SubmitConcept(Guid.NewGuid(), "https://assets.local/rin.png", null);
        v2.Lock(Guid.NewGuid());
        await db.SaveChangesAsync();

        var blockedRename = await addRevHandler.Handle(
            new AddBibleEntryRevisionCommand(
                SeriesId: seriesId,
                BibleEntryId: entry.Id,
                UserId: writerId,
                Summary: "Should fail rename",
                Name: "Another Name"),
            CancellationToken.None);
        blockedRename.IsSuccess.Should().BeFalse();
        blockedRename.ErrorCode.Should().Be(ResultErrorCodes.Validation);
    }

    [Fact]
    public async Task ReopenAndReFreeze_FlagsPanelAssignmentsWithImpactReview_WhenVariantOrBindingChanged()
    {
        var (db, seriesId, _, bible) = CreateContextWithSeries();
        var writerId = Guid.NewGuid();
        var producerId = Guid.NewGuid();
        var artistId = Guid.NewGuid();

        var entry = bible.AddEntry(BibleEntryCategory.Character, "CHR-IMP", "Akira", "Hero");
        var v1 = entry.AddRevision("v1", "{}", "h1", authorId: writerId, isInitialVersion: true);
        v1.SubmitConcept(artistId, "https://assets.local/akira1.png", null);
        v1.Lock(Guid.NewGuid());
        var v2 = entry.AddRevision("v2", "{}", "h2", authorId: writerId, stageName: "Flashback");
        v2.SubmitConcept(artistId, "https://assets.local/akira2.png", null);
        v2.Lock(Guid.NewGuid());

        var chapter = Chapter.Create(seriesId, 1m, "Ch 1");
        var scene = Scene.CreateDefault(chapter.Id);
        var page = Page.CreateNew(chapter.Id, 1, LayoutFormat.StandardPage, 1600, 2400, 300);
        var panel = Panel.Create(page.Id, 1, 1);
        var line = ScriptLine.Create(scene.Id, 1, dialogueText: "Let's go!", speakerLabel: "Akira");
        line.AssignToPanel(panel.Id);

        db.BibleEntries.Add(entry);
        db.BibleEntryRevisions.AddRange(v1, v2);
        db.Chapters.Add(chapter);
        db.Scenes.Add(scene);
        db.Pages.Add(page);
        db.Panels.Add(panel);
        db.ScriptLines.Add(line);
        await db.SaveChangesAsync();

        var inheritanceInit = new ChapterCastInheritanceResolver(db);
        var castServiceInit = new ChapterCastService(db, inheritanceInit);
        var initialCommit = await ScriptSnapshotBuilder.CommitAsync(db, chapter.Id, writerId, "v1", CancellationToken.None, castServiceInit);
        chapter.SubmitScript(writerId);
        chapter.StartProduction(producerId, "frozen-hash", initialCommit.Id);
        await Task.Delay(15);
        chapter.UnfreezeScript(producerId, "Need flashback costume variant");
        await Task.Delay(15);
        chapter.SubmitScript(writerId);

        var binding = db.ChapterBibleBindings.Local.FirstOrDefault(b => b.ChapterId == chapter.Id && b.BibleEntryId == entry.Id)
            ?? await db.ChapterBibleBindings.FirstOrDefaultAsync(b => b.ChapterId == chapter.Id && b.BibleEntryId == entry.Id);
        if (binding == null)
        {
            binding = ChapterBibleBinding.Create(chapter.Id, entry.Id, v1.Id, ChapterBibleBindingSource.Speaker, chosenByWriter: true);
            db.ChapterBibleBindings.Add(binding);
        }
        else
        {
            binding.UpdateBinding(v1.Id, ChapterBibleBindingSource.Speaker, true, writerId);
        }
        var variant = PanelBibleVariant.Create(panel.Id, entry.Id, v2.Id, writerId);
        var panelAssignment = Assignment.Create(
            seriesId, chapter.Id, WorkflowEntityType.Panel, panel.Id, Guid.NewGuid(), artistId, WorkspaceRole.Artist, "Draw Panel 1");

        db.PanelBibleVariants.Add(variant);
        db.Assignments.Add(panelAssignment);
        await db.SaveChangesAsync();

        var castGateMock = new Mock<IChapterCastGate>();
        castGateMock
            .Setup(g => g.EvaluateStartAsync(chapter.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChapterCastStartGateDto(true, []));

        var freezeHandler = new FreezeChapterScriptCommandHandler(db, castGateMock.Object);
        var freezeResult = await freezeHandler.Handle(new FreezeChapterScriptCommand(chapter.Id, producerId), CancellationToken.None);

        freezeResult.IsSuccess.Should().BeTrue();
        panelAssignment.ImpactReviewReason.Should().NotBeNullOrWhiteSpace();
        panelAssignment.ImpactReviewRequestedAt.Should().NotBeNull();
        panelAssignment.HasScriptImpact.Should().BeTrue();
    }
}
