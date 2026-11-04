using FluentAssertions;
using PanelForge.AI.Evaluation;
using Xunit;

namespace PanelForge.UnitTests.Ai;

public class ContinuityEvalTests
{
    private const string Bible = """
        ## 1. Series Bible
        ### 1.1. Nhân vật
        | Code | Tên | Vai trò | Thiết kế | Giọng |
        |---|---|---|---|---|
        | CHR-LAM | Lâm Dư | Nam, 17 tuổi | Tay phải dẫn lửa. | Mộc mạc |
        Mẹ của Lâm Dư đã mất trước truyện.
        ### 1.6. Plot facts
        | FACT-03 | Chương 2, Cảnh 4 | A Dược tặng Sổ Tro. |
        ## 2. Mục lục sản xuất
        | 1 | 2 | Một Vạch Linh Căn | CHR-LAM, CHR-NINH |
        ## 7. Checklist
        - Ngọc Lá luôn ở cổ tay trái.
        """;

    [Fact]
    public void ParseBible_maps_table_rows_notes_and_free_sections()
    {
        var bible = ContinuityEval.ParseBible(Bible);

        bible.Select(b => b.Code).Should().Equal("CHR-LAM", "FACT-03", "NOTE-CHARACTERS", "PROD-INDEX", "CHECKLIST");
        bible[0].Category.Should().Be("Character");
        bible[0].Name.Should().Be("Lâm Dư");
        bible[0].Description.Should().Contain("Tay phải dẫn lửa.");
        bible[1].Category.Should().Be("PlotFact");
        bible[2].Description.Should().Contain("đã mất");
        bible[4].Description.Should().Contain("cổ tay trái");
    }

    [Fact]
    public void BuildCase_splits_speaker_dialogue_and_stage()
    {
        var bible = ContinuityEval.ParseBible(Bible);
        var s = new EvalSample("SMP-X", "Demo Chapter 3", "Character Text",
            "[C03 | Speaker: CHR-LAM] Lâm Dư dẫn lửa. Thoại: \"Mẹ à...\"", "x", null);

        var (ctx, draft) = ContinuityEval.BuildCase(s, bible, "m");

        ctx.ChapterNumber.Should().Be(3);
        var line = draft.Lines.Single();
        line.SpeakerId.Should().Be(bible[0].EntryId);
        line.Dialogue.Should().Be("Mẹ à...");
        line.StageDirection.Should().Be("[C03 | Speaker: CHR-LAM] Lâm Dư dẫn lửa.");
    }

    [Fact]
    public void Score_builds_confusion_matrix_and_skips_failed_calls()
    {
        EvalResult R(bool expected, bool? predicted) => new("id", "c", "t", expected, predicted, null, [], 0, 0);
        var m = ContinuityEval.Score([R(true, true), R(true, false), R(false, true), R(false, false), R(false, false), R(true, null)]);

        m.Should().Be(new EvalMetrics(1, 1, 2, 1, 1));
        m.Recall.Should().Be(0.5);
        m.FalsePositiveRate.Should().BeApproximately(1.0 / 3, 1e-9);
    }
}
