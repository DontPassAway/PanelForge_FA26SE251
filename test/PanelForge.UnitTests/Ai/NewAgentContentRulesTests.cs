using FluentAssertions;
using PanelForge.AI.Contracts;
using Xunit;

namespace PanelForge.UnitTests.Ai;

/// <summary>Validator của code cho output của Artist / Letterer / Producer — nhận định của LLM không bao giờ thay thế các kiểm tra này.</summary>
public class NewAgentContentRulesTests
{
    private static readonly Guid Run = Guid.NewGuid(), Scene = Guid.NewGuid(), LayoutRev = Guid.NewGuid();
    private static readonly Guid An = Guid.NewGuid(), BaLan = Guid.NewGuid(), Cafe = Guid.NewGuid();
    private static readonly Guid Panel1 = Guid.NewGuid(), Panel2 = Guid.NewGuid();

    // ── Artist ───────────────────────────────────────────────────────────────
    private static ArtistContext ArtistCtx() => new(LayoutRev,
        [
            new ArtistPanelContext(Panel1, 1, 1, 1, "wide shot", "Tiệm đồng hồ", [An], []),
            new ArtistPanelContext(Panel2, 1, 2, 2, "close-up", "Bà Lan ngoài hiên", [An, BaLan], [])
        ], []);

    private static ArtistPanelPrompt Prompt(Guid panel, int panelNo, params Guid[] refs) =>
        new(panel, 1, panelNo, "Manga panel, ink", refs, null);

    private static ArtistPromptSet Set(params ArtistPanelPrompt[] panels) =>
        new(AiProtocol.SchemaVersion, Run, Scene, LayoutRev, panels, null);

    private static IReadOnlyList<string> CheckArtist(ArtistPromptSet set) =>
        ContentRules.ValidateArtist(set, Run, Scene, ArtistCtx(), new HashSet<Guid> { An, BaLan, Cafe });

    [Fact]
    public void Artist_ValidSet_Passes() =>
        CheckArtist(Set(Prompt(Panel1, 1, An), Prompt(Panel2, 2, An, BaLan, Cafe))).Should().BeEmpty();

    [Fact]
    public void Artist_MissingPanel_UnknownPanel_Duplicate_AreRejected()
    {
        CheckArtist(Set(Prompt(Panel1, 1, An))).Should().Contain(e => e.Contains("chưa có prompt"));
        CheckArtist(Set(Prompt(Panel1, 1, An), Prompt(Panel2, 2, An, BaLan), Prompt(Guid.NewGuid(), 3, An)))
            .Should().Contain(e => e.Contains("không thuộc layout nền"));
        CheckArtist(Set(Prompt(Panel1, 1, An), Prompt(Panel1, 1, An), Prompt(Panel2, 2, An, BaLan)))
            .Should().Contain(e => e.Contains("hơn một prompt"));
    }

    [Fact]
    public void Artist_ReferenceOutsideBible_IsRejected_AndModelCannotPickItsOwnIds()
    {
        var stray = Guid.NewGuid();
        CheckArtist(Set(Prompt(Panel1, 1, An, stray), Prompt(Panel2, 2, An, BaLan)))
            .Should().Contain(e => e.Contains(stray.ToString()) && e.Contains("Bible"));
    }

    [Fact]
    public void Artist_CharacterInPanelMustBeReferenced()
    {
        CheckArtist(Set(Prompt(Panel1, 1, An), Prompt(Panel2, 2, An)))
            .Should().Contain(e => e.Contains("thiếu tham chiếu nhân vật") && e.Contains(BaLan.ToString()));
    }

    [Fact]
    public void Artist_EmptyOrTooLongPrompt_WrongLayoutRevision_AreRejected()
    {
        var longPrompt = Prompt(Panel1, 1, An) with { Prompt = new string('x', ContentRules.MaxPromptLength + 1) };
        CheckArtist(Set(longPrompt, Prompt(Panel2, 2, An, BaLan))).Should().Contain(e => e.Contains("dài quá"));
        CheckArtist(Set(Prompt(Panel1, 1, An) with { Prompt = " " }, Prompt(Panel2, 2, An, BaLan)))
            .Should().Contain(e => e.Contains("thiếu prompt"));
        CheckArtist(Set(Prompt(Panel1, 1, An), Prompt(Panel2, 2, An, BaLan)) with { LayoutRevisionId = Guid.NewGuid() })
            .Should().Contain(e => e.Contains("layoutRevisionId"));
    }

    // ── Letterer ─────────────────────────────────────────────────────────────
    private static readonly Guid Line1 = Guid.NewGuid(), Line2 = Guid.NewGuid(), Bubble1 = Guid.NewGuid();
    private const string Long = "Bà ơi, hay để cháu thử cạy ổ khóa này bằng chiếc kẹp tóc nhỏ của cháu?";

    private static LetteringContext LetteringCtx() => new(LayoutRev,
    [
        new LetteringLineContext(Line1, 1, An, Long, Panel1, 1, 1, Bubble1, "SPEECH_BALLOON", 30, LetteringLimits.SourceElement),
        new LetteringLineContext(Line2, 2, BaLan, "Khoan đã… chưa đến lúc.", Panel2, 1, 2, null, null, 80, LetteringLimits.SourcePanel)
    ]);

    private static LetteringSuggestion Sug(Guid line, string suggested)
    {
        var ctx = LetteringCtx().Lines.First(l => l.LineId == line);
        return new LetteringSuggestion(line, ctx.ElementId, ctx.PanelId, ctx.Dialogue, suggested, "Lược phần cuối câu",
            ctx.MaxChars, ctx.LimitSource, suggested.Length <= ctx.MaxChars);
    }

    private static IReadOnlyList<string> CheckLettering(params LetteringSuggestion[] s) =>
        ContentRules.ValidateLettering(new LetteringSuggestionSet(AiProtocol.SchemaVersion, Run, Scene, s, null), Run, Scene, LetteringCtx());

    [Fact]
    public void Lettering_ValidShortening_Passes_AndEmptyListIsValid()
    {
        CheckLettering(Sug(Line1, "Bà ơi, để cháu thử cạy khóa?")).Should().BeEmpty();
        CheckLettering().Should().BeEmpty("không dòng nào cần rút gọn là kết quả hợp lệ");
    }

    [Fact]
    public void Lettering_SuggestionMustBeShorter_AndWithinEstimatedLimit()
    {
        CheckLettering(Sug(Line1, Long + " nữa")).Should().Contain(e => e.Contains("ngắn hơn"));
        // ngắn hơn bản gốc nhưng vẫn vượt giới hạn ước lượng 30 ký tự
        CheckLettering(Sug(Line1, "Bà ơi, hay để cháu thử cạy ổ khóa này bằng kẹp tóc")).Should().Contain(e => e.Contains("vượt giới hạn"));
    }

    [Fact]
    public void Lettering_SingleBinding_OneSuggestionPerLine()
    {
        CheckLettering(Sug(Line1, "Bà ơi, để cháu thử?"), Sug(Line1, "Bà ơi, thử nhé?"))
            .Should().Contain(e => e.Contains("hơn một đề xuất"));
    }

    [Fact]
    public void Lettering_LinkageAndOriginalTextMustMatchCodeContext()
    {
        var wrongBubble = Sug(Line1, "Bà ơi, để cháu thử?") with { TargetElementId = Guid.NewGuid() };
        CheckLettering(wrongBubble).Should().Contain(e => e.Contains("targetElementId"));

        var wrongPanel = Sug(Line2, "Khoan đã…") with { PanelId = Panel1 };
        CheckLettering(wrongPanel).Should().Contain(e => e.Contains("panelId"));

        var wrongOriginal = Sug(Line1, "Bà ơi, để cháu thử?") with { OriginalText = "Văn bản khác" };
        CheckLettering(wrongOriginal).Should().Contain(e => e.Contains("originalText"));

        var unknownLine = Sug(Line1, "Bà ơi, để cháu thử?") with { SourceScriptLineId = Guid.NewGuid() };
        CheckLettering(unknownLine).Should().Contain(e => e.Contains("không thuộc script"));
    }

    [Fact]
    public void Lettering_CodeNumbersCannotBeOverriddenByTheModel()
    {
        var lieAboutLimit = Sug(Line1, "Bà ơi, để cháu thử cạy khóa?") with { MaxChars = 500 };
        CheckLettering(lieAboutLimit).Should().Contain(e => e.Contains("giới hạn chữ không khớp"));

        // withinEstimatedLimit là số của code: model không thể khẳng định "vừa" khi vượt ước lượng
        var claimsFit = Sug(Line1, "Bà ơi, hay để cháu thử cạy ổ khóa này bằng kẹp tóc") with { WithinEstimatedLimit = true };
        CheckLettering(claimsFit).Should().Contain(e => e.Contains("withinEstimatedLimit"));
    }

    [Fact]
    public void Lettering_SingleBinding_OneElementPerSuggestion()
    {
        // Hai dòng cùng bị gán một bubble (do ngữ cảnh sai) → bubble chỉ nhận một đề xuất
        var ctx = new LetteringContext(LayoutRev,
        [
            new LetteringLineContext(Line1, 1, An, Long, Panel1, 1, 1, Bubble1, "SPEECH_BALLOON", 60, LetteringLimits.SourceElement),
            new LetteringLineContext(Line2, 2, BaLan, Long + " thêm", Panel1, 1, 1, Bubble1, "SPEECH_BALLOON", 60, LetteringLimits.SourceElement)
        ]);
        var set = new LetteringSuggestionSet(AiProtocol.SchemaVersion, Run, Scene,
        [
            new LetteringSuggestion(Line1, Bubble1, Panel1, Long, "Bà ơi, để cháu thử?", "r", 60, LetteringLimits.SourceElement, true),
            new LetteringSuggestion(Line2, Bubble1, Panel1, Long + " thêm", "Thử nhé?", "r", 60, LetteringLimits.SourceElement, true)
        ], null);
        ContentRules.ValidateLettering(set, Run, Scene, ctx).Should().Contain(e => e.Contains("single-binding"));
    }

    [Fact]
    public void LetteringLimits_AreEstimates_ThatGrowWithTheBox_AndHaveAFloor()
    {
        LetteringLimits.ForElement(300, 200).Should().BeGreaterThan(LetteringLimits.ForElement(150, 100));
        LetteringLimits.ForElement(10, 10).Should().Be(LetteringLimits.MinChars);
        LetteringLimits.ForPanel(600, 594).Should().BeGreaterThan(LetteringLimits.ForPanel(300, 300));
    }

    // ── Producer ─────────────────────────────────────────────────────────────
    private static ProgressContext Progress() => new("Chương 3", DateTime.UtcNow,
    [
        new ProgressFact("F1", "Chương 3: InProduction"),
        new ProgressFact("F2", "Hoàn thành assignment (đã Approved): 2/5 = 40%."),
        new ProgressFact("F3", "Assignment Writer/Script hạn 2026-09-30, đã quá 2 ngày, trạng thái InProgress.")
    ],
    ["Chapter chưa đặt ngày phát hành mục tiêu."], ["40%"]);

    private static ProductionReport Report(
        string summary = "Hoàn thành 40%.", ProductionRisk[]? risks = null, ProductionRecommendation[]? recs = null,
        IReadOnlyList<string>? gaps = null, ProgressContext? ctx = null)
    {
        ctx ??= Progress();
        return new ProductionReport(AiProtocol.SchemaVersion, Run, "Tiêu đề", summary, risks ?? [], recs ?? [], gaps ?? ctx.DataGaps, ctx.Facts);
    }

    private static IReadOnlyList<string> CheckProduction(ProductionReport r) => ContentRules.ValidateProduction(r, Run, Progress());

    [Fact]
    public void Production_CitedFacts_AndCodeComputedPercent_Pass()
    {
        var report = Report("Đã Approved 40 % (xem F2).",
            [new ProductionRisk("Warning", "Assignment Writer đã quá hạn", ["F3"])],
            [new ProductionRecommendation("Hỏi lại deadline", "F3 cho thấy quá hạn", ["F3"], "Producer")]);
        CheckProduction(report).Should().BeEmpty("'40 %' chuẩn hoá thành 40% trùng số do code tính");
    }

    [Fact]
    public void Production_LlmCannotInventPercentages()
    {
        CheckProduction(Report("Chương đã xong 85%.")).Should().Contain(e => e.Contains("85%") && e.Contains("do hệ thống tính"));
        CheckProduction(Report(risks: [new ProductionRisk("Warning", "Trễ 70% công việc", ["F3"])]))
            .Should().Contain(e => e.Contains("70%"));
        CheckProduction(Report(recs: [new ProductionRecommendation("Đẩy nhanh", "Mới đạt 12.5%", ["F2"], null)]))
            .Should().Contain(e => e.Contains("12.5%"));
    }

    [Fact]
    public void Production_EverythingNeedsASourceFromTheSnapshot()
    {
        CheckProduction(Report(risks: [new ProductionRisk("Warning", "Rủi ro không nguồn", [])]))
            .Should().Contain(e => e.Contains("thiếu nguồn"));
        CheckProduction(Report(risks: [new ProductionRisk("Warning", "Nguồn bịa", ["F99"])]))
            .Should().Contain(e => e.Contains("F99"));
        CheckProduction(Report(recs: [new ProductionRecommendation("Làm gì đó", "Vì sao đó", ["F404"], null)]))
            .Should().Contain(e => e.Contains("F404"));
        CheckProduction(Report(risks: [new ProductionRisk("Cực kỳ", "Severity lạ", ["F1"])]))
            .Should().Contain(e => e.Contains("severity"));
    }

    [Fact]
    public void Production_DataGapsMustBeStatedAsComputedByCode()
    {
        CheckProduction(Report(gaps: [])).Should().Contain(e => e.Contains("dataGaps"));
        CheckProduction(Report(gaps: ["Thiếu dữ liệu tự bịa"])).Should().Contain(e => e.Contains("dataGaps"));
    }

    [Theory]
    [InlineData("40 %", "40%")]
    [InlineData("40,0%", "40%")]
    [InlineData("12,50%", "12.5%")]
    [InlineData("100%", "100%")]
    [InlineData("0%", "0%")]
    public void NormalizePercent_IsStable(string raw, string expected) => ContentRules.NormalizePercent(raw).Should().Be(expected);
}
