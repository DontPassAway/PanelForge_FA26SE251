using FluentAssertions;
using PanelForge.AI.Contracts;
using PanelForge.AI.Validators;
using Xunit;

namespace PanelForge.UnitTests.Ai;

public class ContentRulesTests
{
    private static readonly Guid RunId = Guid.NewGuid();
    private static readonly Guid SceneId = Guid.NewGuid();
    private static readonly Guid L1 = Guid.NewGuid();
    private static readonly Guid L2 = Guid.NewGuid();
    private static readonly Guid An = Guid.NewGuid();

    private static ScriptDraft Script() => new(1, RunId, SceneId, 0,
        [new ScriptDraftLine(L1, 1, null, null, "Mở cảnh"), new ScriptDraftLine(L2, 2, An, "Xin chào", null)], [], null);

    private static LayoutPanel Panel(int n, int order, Bbox box, params Guid[] lines) =>
        new(n, order, box, AiProtocol.PagePixelSpace, "wide", "mô tả", [], lines);

    private static StoryboardLayout Layout(string dir, params LayoutPage[] pages) =>
        new(1, RunId, SceneId, dir, pages, null);

    private static IReadOnlyList<string> Validate(StoryboardLayout l, string dir = "RightToLeft") =>
        ContentRules.ValidateLayout(l, RunId, SceneId, Script(), dir, new HashSet<Guid> { An });

    [Theory]
    [InlineData(0.5, 0.0, 0.6, 0.5)]            // x + w > 1
    [InlineData(0.0, 0.7, 0.5, 0.4)]            // y + h > 1
    [InlineData(0.0, 0.0, 0.0, 0.5)]            // w = 0
    [InlineData(0.0, 0.0, 0.5, -0.1)]           // h âm
    [InlineData(-0.1, 0.0, 0.5, 0.5)]           // x âm
    [InlineData(0.0, 0.0, 1.5, 0.5)]            // w > 1
    [InlineData(double.NaN, 0.0, 0.5, 0.5)]     // không hữu hạn
    [InlineData(0.0, 0.0, double.PositiveInfinity, 0.5)]
    public void Bbox_Invalid_IsRejected(double x, double y, double w, double h) =>
        ContentRules.ValidateBbox(new Bbox(x, y, w, h), "p").Should().NotBeEmpty();

    [Theory]
    [InlineData(0.0, 0.0, 1.0, 1.0)]
    [InlineData(0.5, 0.5, 0.5, 0.5)]
    [InlineData(0.25, 0.1, 0.5, 0.3)]
    public void Bbox_Valid_IsAccepted(double x, double y, double w, double h) =>
        ContentRules.ValidateBbox(new Bbox(x, y, w, h), "p").Should().BeEmpty();

    [Fact]
    public void Layout_RtlRowReadRightToLeft_IsValid_AndLtrOrderIsRejectedForRtl()
    {
        var right = new Bbox(0.5, 0, 0.5, 0.5);
        var left = new Bbox(0, 0, 0.5, 0.5);
        var rtlOk = Layout("RightToLeft", new LayoutPage(1, [Panel(1, 1, right, L1), Panel(2, 2, left, L2)]));
        var rtlWrong = Layout("RightToLeft", new LayoutPage(1, [Panel(1, 1, left, L1), Panel(2, 2, right, L2)]));
        var ltrOk = Layout("LeftToRight", new LayoutPage(1, [Panel(1, 1, left, L1), Panel(2, 2, right, L2)]));

        Validate(rtlOk).Should().BeEmpty();
        Validate(rtlWrong).Should().Contain(e => e.Contains("sai hướng"));
        Validate(ltrOk, "LeftToRight").Should().BeEmpty();
        Validate(ltrOk, "RightToLeft").Should().Contain(e => e.Contains("sai hướng") || e.Contains("readingDirection"));
    }

    [Fact]
    public void Layout_ReadingOrderMustBeUniqueAndContiguous()
    {
        var a = new Bbox(0, 0, 1, 0.5);
        var b = new Bbox(0, 0.5, 1, 0.5);
        var dup = Layout("RightToLeft", new LayoutPage(1, [Panel(1, 1, a, L1), Panel(2, 1, b, L2)]));
        var gap = Layout("RightToLeft", new LayoutPage(1, [Panel(1, 1, a, L1), Panel(2, 3, b, L2)]));

        Validate(dup).Should().Contain(e => e.Contains("readingOrder"));
        Validate(gap).Should().Contain(e => e.Contains("readingOrder"));
    }

    [Fact]
    public void Layout_VerticalStackOrder_BottomBeforeTop_IsRejected()
    {
        var top = new Bbox(0, 0, 1, 0.5);
        var bottom = new Bbox(0, 0.5, 1, 0.5);

        Validate(Layout("RightToLeft", new LayoutPage(1, [Panel(1, 1, bottom, L1), Panel(2, 2, top, L2)])))
            .Should().Contain(e => e.Contains("sai hướng"));
    }

    [Fact]
    public void Layout_IdsOutsideScope_AreRejected()
    {
        var foreignLine = Layout("RightToLeft", new LayoutPage(1, [Panel(1, 1, new Bbox(0, 0, 1, 1), L1, Guid.NewGuid())]));
        var foreignChar = Layout("RightToLeft", new LayoutPage(1, [
            new LayoutPanel(1, 1, new Bbox(0, 0, 1, 1), AiProtocol.PagePixelSpace, "wide", "d", [Guid.NewGuid()], [L1, L2])]));
        var wrongScene = Layout("RightToLeft", new LayoutPage(1, [Panel(1, 1, new Bbox(0, 0, 1, 1), L1, L2)])) with { SceneId = Guid.NewGuid() };
        var wrongRun = Layout("RightToLeft", new LayoutPage(1, [Panel(1, 1, new Bbox(0, 0, 1, 1), L1, L2)])) with { RunId = Guid.NewGuid() };

        Validate(foreignLine).Should().Contain(e => e.Contains("không tồn tại trong script"));
        Validate(foreignChar).Should().Contain(e => e.Contains("characterId"));
        Validate(wrongScene).Should().Contain(e => e.Contains("sceneId"));
        Validate(wrongRun).Should().Contain(e => e.Contains("runId"));
    }

    [Fact]
    public void Layout_MustCoverEveryScriptLine_AndCarryCoordinateSpace()
    {
        var partial = Layout("RightToLeft", new LayoutPage(1, [Panel(1, 1, new Bbox(0, 0, 1, 1), L1)]));
        var badSpace = Layout("RightToLeft", new LayoutPage(1, [
            new LayoutPanel(1, 1, new Bbox(0, 0, 1, 1), "pixels", "wide", "d", [], [L1, L2])]));

        Validate(partial).Should().Contain(e => e.Contains("chưa được panel nào tham chiếu"));
        Validate(badSpace).Should().Contain(e => e.Contains("coordinateSpace"));
    }

    [Fact]
    public void Script_RejectsSpeakerOutsideBible_DuplicateIds_AndEmptyLines()
    {
        var okSpeakers = new HashSet<Guid> { An };
        var badSpeaker = Script() with { Lines = [new ScriptDraftLine(L1, 1, Guid.NewGuid(), "x", null)] };
        var dupId = Script() with { Lines = [new ScriptDraftLine(L1, 1, null, "a", null), new ScriptDraftLine(L1, 2, null, "b", null)] };
        var empty = Script() with { Lines = [new ScriptDraftLine(L1, 1, null, "  ", null)] };

        ContentRules.ValidateScript(Script(), RunId, SceneId, okSpeakers, new HashSet<Guid>()).Should().BeEmpty();
        ContentRules.ValidateScript(badSpeaker, RunId, SceneId, okSpeakers, new HashSet<Guid>()).Should().Contain(e => e.Contains("speakerId"));
        ContentRules.ValidateScript(dupId, RunId, SceneId, okSpeakers, new HashSet<Guid>()).Should().Contain(e => e.Contains("trùng"));
        ContentRules.ValidateScript(empty, RunId, SceneId, okSpeakers, new HashSet<Guid>()).Should().Contain(e => e.Contains("dialogue hoặc stageDirection"));
    }

    [Fact]
    public void Finding_NeedsScriptLine_AndEvidenceFromBible_WithQuoteThatReallyExists()
    {
        var bibleRev = Guid.NewGuid();
        var bibleEntry = Guid.NewGuid();
        var bible = new Dictionary<Guid, (Guid, string)> { [bibleRev] = (bibleEntry, "An\nAn chỉ nhận được chìa khóa bạc từ Chương 5.") };
        ContinuityReport Report(params ContinuityFinding[] f) => new(1, RunId, 0, f, "");
        ContinuityFinding Finding(Guid[] lines, BibleEvidence[] ev) => new("F1", "Blocking", "ItemTimeline", "mô tả", lines, ev, null);

        var good = Report(Finding([L1], [new BibleEvidence(bibleEntry, bibleRev, "chỉ nhận được  chìa khóa bạc từ chương 5")]));
        var noLines = Report(Finding([], [new BibleEvidence(bibleEntry, bibleRev, "chìa khóa bạc")]));
        var noEvidence = Report(Finding([L1], []));
        var fakeQuote = Report(Finding([L1], [new BibleEvidence(bibleEntry, bibleRev, "An đã có chìa khóa từ Chương 1")]));
        var foreignRevision = Report(Finding([L1], [new BibleEvidence(bibleEntry, Guid.NewGuid(), "x")]));
        var foreignLine = Report(Finding([Guid.NewGuid()], [new BibleEvidence(bibleEntry, bibleRev, "chìa khóa bạc")]));

        ContentRules.ValidateContinuity(good, RunId, Script(), bible).Should().BeEmpty();
        ContentRules.ValidateContinuity(noLines, RunId, Script(), bible).Should().Contain(e => e.Contains("script line"));
        ContentRules.ValidateContinuity(noEvidence, RunId, Script(), bible).Should().Contain(e => e.Contains("bằng chứng"));
        ContentRules.ValidateContinuity(fakeQuote, RunId, Script(), bible).Should().Contain(e => e.Contains("trích dẫn"));
        ContentRules.ValidateContinuity(foreignRevision, RunId, Script(), bible).Should().Contain(e => e.Contains("không thuộc snapshot"));
        ContentRules.ValidateContinuity(foreignLine, RunId, Script(), bible).Should().Contain(e => e.Contains("không tồn tại trong script"));
    }

    [Fact]
    public void BibleEntryContext_IncludesDetailsInPromptBlock_AndEvidenceText()
    {
        var entry = new BibleEntryContext(Guid.NewGuid(), "CHIA_KHOA", "Prop", "Chìa khóa bạc",
            "Đạo cụ mở tủ đồng hồ.", Guid.NewGuid(), 1, 1, """{"availableFromChapter": 5}""");
        var ctx = new AgentContext(RunId, Guid.NewGuid(), Guid.NewGuid(), 3, SceneId, "H", null, "b", "RightToLeft",
            0, "Mock", "m", 2, [entry], [entry.RevisionId], []);
        var maps = new PanelForge.AI.Agents.KeyMaps(ctx);

        var block = PanelForge.AI.Agents.PromptText.BibleBlock(ctx, maps);
        block.Should().Contain("details: {\"availableFromChapter\": 5}");
        entry.EvidenceText.Should().Contain("availableFromChapter");

        var bible = new Dictionary<Guid, (Guid, string)> { [entry.RevisionId] = (entry.EntryId, entry.EvidenceText) };
        var report = new ContinuityReport(1, RunId, 0,
            [new ContinuityFinding("F1", "Blocking", "ItemTimeline", "dùng trước chương 5", [L1],
                [new BibleEvidence(entry.EntryId, entry.RevisionId, "\"availableFromChapter\": 5")], null)], "");
        ContentRules.ValidateContinuity(report, RunId, Script(), bible).Should().BeEmpty();
    }

    [Fact]
    public void OutputParser_ToleratesCodeFences_RejectsGarbage_AndDetectsForbiddenProperty()
    {
        OutputParser.Parse<PanelForge.AI.Agents.WriterOutput>("```json\n{\"lines\":[{\"speaker\":null,\"dialogue\":\"hi\"}]}\n```", out var e1)
            .Should().NotBeNull();
        e1.Should().BeNull();
        OutputParser.Parse<PanelForge.AI.Agents.WriterOutput>("không có json", out var e2).Should().BeNull();
        e2.Should().NotBeNull();
        OutputParser.Parse<PanelForge.AI.Agents.WriterOutput>("{ \"lines\": [ }", out var e3).Should().BeNull();
        e3.Should().Contain("JSON không hợp lệ");

        OutputParser.HasProperty("{\"pages\":[{\"panels\":[{\"elements\":[]}]}]}", "elements").Should().BeTrue();
        OutputParser.HasProperty("{\"pages\":[{\"panels\":[{}]}]}", "elements").Should().BeFalse();
    }
}