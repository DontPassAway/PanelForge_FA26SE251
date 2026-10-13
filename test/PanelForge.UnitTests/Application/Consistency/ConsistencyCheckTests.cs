using FluentAssertions;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Consistency.Commands;
using PanelForge.Application.Features.Consistency.Common;
using PanelForge.Domain.Entities.Bible;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;
using PanelForge.Infrastructure.Persistence;
using PanelForge.UnitTests.Common;
using Xunit;

namespace PanelForge.UnitTests.Application.Consistency;

/// <summary>CF3 bước 4–5: kiểm tra nhất quán bằng code, ghép finding theo fingerprint, triage (BR-15, BR-21).</summary>
public class ConsistencyCheckTests
{
    private sealed class Fixture
    {
        public readonly PanelForgeDbContext Db = InMemoryDb.Create();
        public readonly Guid UserId = Guid.NewGuid();
        public readonly Chapter Chapter;
        public readonly Scene Scene;
        public readonly BibleEntry Mira;
        public readonly BibleEntry Lighthouse;
        public readonly BibleEntry FutureVillain;

        public Fixture()
        {
            var seriesId = Guid.NewGuid();
            Chapter = Chapter.Create(seriesId, 1);
            Scene = Scene.Create(Chapter.Id, 1);
            var bible = SeriesBible.Create(seriesId, Guid.NewGuid(), "Bible");
            Mira = bible.AddEntry(BibleEntryCategory.Character, "MIRA", "Mira", "Nhân vật chính");
            Mira.AddRevision("v1", "{}", "h1", effectiveFromChapterNumber: 1, isInitialVersion: true).Lock(UserId);
            Lighthouse = bible.AddEntry(BibleEntryCategory.Location, "LIGHTHOUSE", "Hải đăng", "Bối cảnh");
            FutureVillain = bible.AddEntry(BibleEntryCategory.Character, "VILLAIN", "Kẻ phản diện", "Xuất hiện từ chương 3");
            FutureVillain.AddRevision("v1", "{}", "h2", effectiveFromChapterNumber: 3, isInitialVersion: true);

            Db.Chapters.Add(Chapter);
            Db.Scenes.Add(Scene);
            Db.SeriesBibles.Add(bible);
            Db.SaveChanges();
        }

        public ScriptLine Line(int order, string? dialogue, Guid? speaker = null, string? direction = null)
        {
            var line = ScriptLine.Create(Scene.Id, order, dialogue, direction, speaker);
            Db.ScriptLines.Add(line);
            Db.SaveChanges();
            return line;
        }

        public Task<Result<PanelForge.Application.Features.Consistency.Models.ConsistencyCheckResultDto>> Run() =>
            new RunConsistencyCheckCommandHandler(Db).Handle(new RunConsistencyCheckCommand(Chapter.Id, UserId), default);
    }

    [Fact]
    public async Task Check_FindsBuiltInViolations_WithEvidence()
    {
        var f = new Fixture();
        f.Line(1, "Chào chú!", f.Mira.Id);                 // hợp lệ
        f.Line(2, "Ai đó?");                                // SPEAKER_MISSING
        f.Line(3, "Ta là ngọn hải đăng.", f.Lighthouse.Id); // SPEAKER_NOT_CHARACTER
        f.Line(4, "Ha ha ha.", f.FutureVillain.Id);         // ENTRY_NOT_YET_EFFECTIVE (chương 3 > 1)

        var result = await f.Run();

        result.IsSuccess.Should().BeTrue();
        result.Value!.Findings.Select(x => x.RuleCode).Should().BeEquivalentTo(
            ConsistencyChecker.SpeakerMissing, ConsistencyChecker.SpeakerNotCharacter, ConsistencyChecker.EntryNotYetEffective);
        result.Value.NewCount.Should().Be(3);
        result.Value.BlockingOpenCount.Should().Be(2);
        result.Value.Findings.Should().OnlyContain(x => x.Status == ConsistencyFindingStatus.Open && x.Evidence != null);
    }

    [Fact]
    public async Task Check_CharacterWithoutLockedSheet_IsOneNonBlockingWarningPerChapter()
    {
        var f = new Fixture();
        var draft = f.Db.SeriesBibles.Local.Single().AddEntry(BibleEntryCategory.Character, "NAM", "Nam", "Bạn của Mira");
        draft.AddRevision("v1", "{}", "h3", effectiveFromChapterNumber: 1, isInitialVersion: true);
        f.Db.BibleEntries.Add(draft);
        await f.Db.SaveChangesAsync();
        f.Line(1, "Chào Mira!", draft.Id);
        f.Line(2, "Đi đâu đấy?", draft.Id);

        var result = await f.Run();

        var finding = result.Value!.Findings.Should().ContainSingle().Which;
        finding.RuleCode.Should().Be(ConsistencyChecker.CharacterNotLocked);
        finding.Severity.Should().Be(ConsistencyFindingSeverity.Warning);
        finding.BibleEntryId.Should().Be(draft.Id);
        result.Value.BlockingOpenCount.Should().Be(0, "chưa khoá không chặn duyệt chương");
    }

    [Fact]
    public async Task Check_ConfiguredPatternAndLengthRules()
    {
        var f = new Fixture();
        f.Db.ConsistencyRules.Add(ConsistencyRule.Create(f.Chapter.SeriesId, "Không dùng 'OK'", "StrictSpelling",
            pattern: @"\bok\b", severity: "Error"));
        f.Db.ConsistencyRules.Add(ConsistencyRule.Create(f.Chapter.SeriesId, "Thoại ngắn", "DialogueOverflow",
            severity: "Warning", configurationJson: """{"maxChars": 10}"""));
        f.Db.ConsistencyRules.Add(ConsistencyRule.Create(f.Chapter.SeriesId, "Regex hỏng", "Custom", pattern: "(("));
        await f.Db.SaveChangesAsync();
        f.Line(1, "Ok, đi thôi nào mọi người!", f.Mira.Id);

        var result = await f.Run();

        result.Value!.Findings.Should().HaveCount(2);
        result.Value.Findings.Should().Contain(x => x.RuleCode == ConsistencyChecker.RulePattern
                                                    && x.Severity == ConsistencyFindingSeverity.Blocking);
        result.Value.Findings.Should().Contain(x => x.RuleCode == ConsistencyChecker.DialogueTooLong);
    }

    [Fact]
    public async Task Rerun_KeepsIgnored_ResolvesConfirmedFixed_AndStalesOpen()
    {
        var f = new Fixture();
        var missing = f.Line(1, "Ai đó?");
        f.Line(2, "Ta là ngọn hải đăng.", f.Lighthouse.Id);
        var villainLine = f.Line(3, "Ha ha ha.", f.FutureVillain.Id);
        var first = await f.Run();
        var byCode = first.Value!.Findings.ToDictionary(x => x.RuleCode);

        var triage = new TriageConsistencyFindingCommandHandler(f.Db);
        (await triage.Handle(new(f.Chapter.Id, byCode[ConsistencyChecker.SpeakerNotCharacter].Id, f.UserId,
            ConsistencyFindingStatus.Ignored, "Hải đăng biết nói trong giấc mơ của Mira"), default)).IsSuccess.Should().BeTrue();
        (await triage.Handle(new(f.Chapter.Id, byCode[ConsistencyChecker.SpeakerMissing].Id, f.UserId,
            ConsistencyFindingStatus.Confirmed, null), default)).IsSuccess.Should().BeTrue();

        // Writer sửa: bỏ dòng thiếu người nói và dòng phản diện xuất hiện sớm
        f.Db.ScriptLines.Remove(missing);
        f.Db.ScriptLines.Remove(villainLine);
        await f.Db.SaveChangesAsync();

        var second = await f.Run();

        var after = second.Value!.Findings.ToDictionary(x => x.RuleCode, x => x.Status);
        after[ConsistencyChecker.SpeakerNotCharacter].Should().Be(ConsistencyFindingStatus.Ignored, "đã bỏ qua thì không báo lại");
        after[ConsistencyChecker.SpeakerMissing].Should().Be(ConsistencyFindingStatus.Resolved);
        after[ConsistencyChecker.EntryNotYetEffective].Should().Be(ConsistencyFindingStatus.Stale);
        second.Value.NewCount.Should().Be(0);
        second.Value.OpenCount.Should().Be(0);
    }

    [Fact]
    public async Task Triage_IgnoredWithoutReason_IsRejected_AndClosedFindingCannotBeTriagedAgain()
    {
        var f = new Fixture();
        f.Line(1, "Ai đó?");
        var finding = (await f.Run()).Value!.Findings.Single();
        var triage = new TriageConsistencyFindingCommandHandler(f.Db);

        var noReason = await triage.Handle(new(f.Chapter.Id, finding.Id, f.UserId, ConsistencyFindingStatus.Ignored, " "), default);
        noReason.ErrorCode.Should().Be(ResultErrorCodes.Validation);
        noReason.ErrorMessage.Should().Contain("BR-21");

        (await triage.Handle(new(f.Chapter.Id, finding.Id, f.UserId, ConsistencyFindingStatus.Rejected, null), default))
            .IsSuccess.Should().BeTrue();
        (await triage.Handle(new(f.Chapter.Id, finding.Id, f.UserId, ConsistencyFindingStatus.Confirmed, null), default))
            .ErrorCode.Should().Be(ResultErrorCodes.Conflict);
    }

    [Fact]
    public async Task Run_UnknownChapter_IsNotFound()
    {
        var f = new Fixture();
        var result = await new RunConsistencyCheckCommandHandler(f.Db)
            .Handle(new RunConsistencyCheckCommand(Guid.NewGuid(), f.UserId), default);
        result.ErrorCode.Should().Be(ResultErrorCodes.NotFound);
    }

    [Fact]
    public async Task Check_CharacterAfterDeath_BlocksSpokenLinesAfterDeathChapter()
    {
        var f = new Fixture();
        // Tạo nhân vật chết ở chương 1, kiểm tra ở chương 2
        var bible = f.Db.SeriesBibles.Local.Single();
        var ghost = bible.AddEntry(BibleEntryCategory.Character, "GHOST", "Hồn Ma", "Đã chết",
            detailsJson: """{"diesAtChapter": 1}""");
        ghost.AddRevision("v1", "{}", "hg1", effectiveFromChapterNumber: 1, isInitialVersion: true).Lock(f.UserId);
        f.Db.BibleEntries.Add(ghost);

        var ch2 = Chapter.Create(f.Chapter.SeriesId, 2);
        var sc2 = Scene.Create(ch2.Id, 1);
        f.Db.Chapters.Add(ch2);
        f.Db.Scenes.Add(sc2);
        var line = ScriptLine.Create(sc2.Id, 1, "Tôi vẫn ở đây...", speakerCharacterId: ghost.Id);
        f.Db.ScriptLines.Add(line);
        await f.Db.SaveChangesAsync();

        var result = await new RunConsistencyCheckCommandHandler(f.Db)
            .Handle(new RunConsistencyCheckCommand(ch2.Id, f.UserId), default);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Findings.Should().ContainSingle(x =>
            x.RuleCode == ConsistencyChecker.CharacterAfterDeath
            && x.Severity == ConsistencyFindingSeverity.Blocking
            && x.BibleEntryId == ghost.Id);
    }

    [Fact]
    public async Task Check_PropNotBefore_BlocksPropUsageBeforeAllowedChapter()
    {
        var f = new Fixture();
        // Chìa khóa bạc chỉ xuất hiện từ chương 5 (availableFromChapter: 5), dùng ở chương 1
        var bible = f.Db.SeriesBibles.Local.Single();
        var silverKey = bible.AddEntry(BibleEntryCategory.Prop, "CHIA_KHOA_BAC", "Chìa khóa bạc", "Đạo cụ bí mật",
            detailsJson: """{"availableFromChapter": 5}""");
        silverKey.AddRevision("v1", "{}", "hk1", effectiveFromChapterNumber: 1, isInitialVersion: true).Lock(f.UserId);
        f.Db.BibleEntries.Add(silverKey);

        var line = f.Line(1, "Tôi tìm thấy chiếc chìa khóa này.", f.Mira.Id);
        f.Db.ScriptLineBibleLinks.Add(ScriptLineBibleLink.Create(line.Id, silverKey.Id));
        await f.Db.SaveChangesAsync();

        var result = await f.Run();

        result.IsSuccess.Should().BeTrue();
        result.Value!.Findings.Should().Contain(x =>
            x.RuleCode == ConsistencyChecker.PropNotBefore
            && x.Severity == ConsistencyFindingSeverity.Blocking
            && x.BibleEntryId == silverKey.Id);
    }

    [Fact]
    public async Task Check_KnowledgeBeforeReveal_BlocksCharacterSpeakingAboutUnknownSecret()
    {
        var f = new Fixture();
        // Bí mật chỉ An biết từ chương 5, Mira nói ở chương 1
        var bible = f.Db.SeriesBibles.Local.Single();
        var secret = bible.AddEntry(BibleEntryCategory.PlotFact, "SECRET", "Bí mật cổ đại", "Sự thật",
            detailsJson: """{"knownBy": [{"character": "AN", "fromChapter": 5}]}""");
        secret.AddRevision("v1", "{}", "hs1", effectiveFromChapterNumber: 1, isInitialVersion: true).Lock(f.UserId);
        f.Db.BibleEntries.Add(secret);

        var line = f.Line(1, "Tôi biết về bí mật cổ đại!", f.Mira.Id);
        f.Db.ScriptLineBibleLinks.Add(ScriptLineBibleLink.Create(line.Id, secret.Id));
        await f.Db.SaveChangesAsync();

        var result = await f.Run();

        result.IsSuccess.Should().BeTrue();
        result.Value!.Findings.Should().Contain(x =>
            x.RuleCode == ConsistencyChecker.KnowledgeBeforeReveal
            && x.Severity == ConsistencyFindingSeverity.Blocking
            && x.BibleEntryId == secret.Id);
    }

    [Fact]
    public async Task Check_ReadingOrderWrong_WarnsWhenPanelReadingOrderConflictsWithScript()
    {
        var f = new Fixture();
        var page = Page.CreateNew(f.Chapter.Id, 1, LayoutFormat.StandardPage, 800, 1200, 300);
        var p1 = Panel.Create(page.Id, 1, readingOrder: 2); // dòng 1 ở panel đọc thứ 2
        var p2 = Panel.Create(page.Id, 2, readingOrder: 1); // dòng 2 ở panel đọc thứ 1 (ngược thứ tự!)
        f.Db.Pages.Add(page);
        f.Db.Panels.AddRange(p1, p2);

        var line1 = f.Line(1, "Câu nói đầu tiên.", f.Mira.Id);
        var line2 = f.Line(2, "Câu trả lời sau.", f.Mira.Id);
        line1.AssignToPanel(p1.Id);
        line2.AssignToPanel(p2.Id);
        await f.Db.SaveChangesAsync();

        var result = await f.Run();

        result.IsSuccess.Should().BeTrue();
        result.Value!.Findings.Should().Contain(x =>
            x.RuleCode == ConsistencyChecker.ReadingOrderWrong
            && x.Severity == ConsistencyFindingSeverity.Warning);
    }

    [Fact]
    public void ConsistencyChecker_DirectCheck_DetectsAllNewRules()
    {
        var lineId1 = Guid.NewGuid();
        var lineId2 = Guid.NewGuid();
        var speakerId = Guid.NewGuid();
        var propId = Guid.NewGuid();
        var sceneId = Guid.NewGuid();

        var entries = new Dictionary<Guid, CheckEntry>
        {
            [speakerId] = new(speakerId, "Arthur", BibleEntryCategory.Character, true, 1, diesAtChapter: 2),
            [propId] = new(propId, "Holy Grail", BibleEntryCategory.Prop, true, 1, availableFromChapter: 5)
        };

        var lines = new List<CheckLine>
        {
            new(lineId1, sceneId, 1, 1, "Where is the grail?", null, speakerId, [propId],
                PanelId: Guid.NewGuid(), PanelReadingOrder: 2, PageNumber: 1),
            new(lineId2, sceneId, 1, 2, "I found it!", null, speakerId, [propId],
                PanelId: Guid.NewGuid(), PanelReadingOrder: 1, PageNumber: 1)
        };

        // Ở chương 3: Arthur đã chết (chương 2), Grail chỉ từ chương 5, và thứ tự panel ngược
        var findings = ConsistencyChecker.Check(3, lines, entries, []);

        findings.Should().Contain(x => x.RuleCode == ConsistencyChecker.CharacterAfterDeath);
        findings.Should().Contain(x => x.RuleCode == ConsistencyChecker.PropNotBefore);
        findings.Should().Contain(x => x.RuleCode == ConsistencyChecker.ReadingOrderWrong);
    }

    [Fact]
    public void ConsistencyChecker_Flashback_SuppressesCharacterAfterDeath()
    {
        var speakerId = Guid.NewGuid();
        var entries = new Dictionary<Guid, CheckEntry>
        {
            [speakerId] = new(speakerId, "Arthur", BibleEntryCategory.Character, true, 1, diesAtChapter: 2)
        };

        var lines = new List<CheckLine>
        {
            new(Guid.NewGuid(), Guid.NewGuid(), 1, 1, "Remembering old times", null, speakerId, [],
                NarrationMode: NarrationMode.Flashback)
        };

        var findings = ConsistencyChecker.Check(3, lines, entries, []);

        findings.Should().NotContain(x => x.RuleCode == ConsistencyChecker.CharacterAfterDeath);
    }

    [Fact]
    public void ConsistencyChecker_DreamAndImagination_SuppressDeathPropAndKnowledgeRules()
    {
        var speakerId = Guid.NewGuid();
        var propId = Guid.NewGuid();
        var secretId = Guid.NewGuid();

        var entries = new Dictionary<Guid, CheckEntry>
        {
            [speakerId] = new(speakerId, "Arthur", BibleEntryCategory.Character, true, 1, diesAtChapter: 2),
            [propId] = new(propId, "Holy Grail", BibleEntryCategory.Prop, true, 1, availableFromChapter: 5),
            [secretId] = new(secretId, "Dark Secret", BibleEntryCategory.PlotFact, true, 1, availableFromChapter: 10)
        };

        var dreamLines = new List<CheckLine>
        {
            new(Guid.NewGuid(), Guid.NewGuid(), 1, 1, "In my dream...", null, speakerId, [propId, secretId],
                NarrationMode: NarrationMode.Dream)
        };

        var dreamFindings = ConsistencyChecker.Check(3, dreamLines, entries, []);
        dreamFindings.Should().NotContain(x => x.RuleCode == ConsistencyChecker.CharacterAfterDeath);
        dreamFindings.Should().NotContain(x => x.RuleCode == ConsistencyChecker.PropNotBefore);
        dreamFindings.Should().NotContain(x => x.RuleCode == ConsistencyChecker.KnowledgeBeforeReveal);

        var imagineLines = new List<CheckLine>
        {
            new(Guid.NewGuid(), Guid.NewGuid(), 1, 1, "What if...", null, speakerId, [propId, secretId],
                NarrationMode: NarrationMode.Imagination)
        };

        var imagineFindings = ConsistencyChecker.Check(3, imagineLines, entries, []);
        imagineFindings.Should().NotContain(x => x.RuleCode == ConsistencyChecker.CharacterAfterDeath);
        imagineFindings.Should().NotContain(x => x.RuleCode == ConsistencyChecker.PropNotBefore);
        imagineFindings.Should().NotContain(x => x.RuleCode == ConsistencyChecker.KnowledgeBeforeReveal);
    }

    [Fact]
    public void ConsistencyChecker_LieAndRumor_SuppressKnowledgeBeforeRevealOnly()
    {
        var speakerId = Guid.NewGuid();
        var secretId = Guid.NewGuid();

        var entries = new Dictionary<Guid, CheckEntry>
        {
            [speakerId] = new(speakerId, "Arthur", BibleEntryCategory.Character, true, 1, diesAtChapter: 2),
            [secretId] = new(secretId, "Secret Identity", BibleEntryCategory.PlotFact, true, 1, availableFromChapter: 8)
        };

        var lieLines = new List<CheckLine>
        {
            new(Guid.NewGuid(), Guid.NewGuid(), 1, 1, "I'm lying about the secret", null, speakerId, [secretId],
                NarrationMode: NarrationMode.Lie)
        };

        var findings = ConsistencyChecker.Check(3, lieLines, entries, []);

        // Lie suppresses KNOWLEDGE_BEFORE_REVEAL
        findings.Should().NotContain(x => x.RuleCode == ConsistencyChecker.KnowledgeBeforeReveal);
        // But CHARACTER_AFTER_DEATH is NOT suppressed for Lie
        findings.Should().Contain(x => x.RuleCode == ConsistencyChecker.CharacterAfterDeath);
    }

    [Fact]
    public void ConsistencyChecker_UnreliableNarrator_DowngradesToWarning()
    {
        var speakerId = Guid.NewGuid();
        var entries = new Dictionary<Guid, CheckEntry>
        {
            [speakerId] = new(speakerId, "Arthur", BibleEntryCategory.Character, true, 1, diesAtChapter: 2)
        };

        var lines = new List<CheckLine>
        {
            new(Guid.NewGuid(), Guid.NewGuid(), 1, 1, "Or did he?", null, speakerId, [],
                NarrationMode: NarrationMode.UnreliableNarrator)
        };

        var findings = ConsistencyChecker.Check(3, lines, entries, []);

        var deathFinding = findings.FirstOrDefault(x => x.RuleCode == ConsistencyChecker.CharacterAfterDeath);
        deathFinding.Should().NotBeNull();
        deathFinding!.Severity.Should().Be(ConsistencyFindingSeverity.Warning);
    }
}

