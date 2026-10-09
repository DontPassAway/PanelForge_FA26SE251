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
            Mira.AddRevision("v1", "{}", "h1", effectiveFromChapterNumber: 1, isInitialVersion: true);
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
}
