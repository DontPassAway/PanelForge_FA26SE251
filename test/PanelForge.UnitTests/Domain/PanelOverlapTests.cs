using FluentAssertions;
using PanelForge.Domain.Services;
using Xunit;

namespace PanelForge.UnitTests.Domain;

public class PanelOverlapTests
{
    [Fact]
    public void FindIntersectingPanelIds_BoxSplitAcrossTwoPanels_ReturnsBoth()
    {
        var left = Guid.NewGuid();
        var right = Guid.NewGuid();

        var result = PanelOverlap.FindIntersectingPanelIds(
            """{"x":0.4,"y":0.2,"w":0.2,"h":0.2}""",
            [
                (left, """{"x":0,"y":0,"w":0.5,"h":1}"""),
                (right, """{"x":0.5,"y":0,"w":0.5,"h":1}""")
            ]);

        result.Should().Equal(left, right);
    }

    [Fact]
    public void FindIntersectingPanelIds_TenPercentOverlap_ExcludesThatPanel()
    {
        var left = Guid.NewGuid();
        var right = Guid.NewGuid();

        var result = PanelOverlap.FindIntersectingPanelIds(
            """{"x":0.45,"y":0.2,"w":0.5,"h":0.2}""",
            [
                (left, """{"x":0,"y":0,"w":0.5,"h":1}"""),
                (right, """{"x":0.5,"y":0,"w":0.5,"h":1}""")
            ]);

        result.Should().Equal(right);
    }

    [Fact]
    public void FindIntersectingPanelIds_LargeErrorBoxFullyCoveringSmallPanel_SelectsThatPanel()
    {
        // D8: tỷ lệ tính trên diện tích nhỏ hơn (Panel), nên vùng lỗi lớn phủ trọn Panel nhỏ vẫn chọn Panel đó.
        var small = Guid.NewGuid();

        var result = PanelOverlap.FindIntersectingPanelIds(
            """{"x":0,"y":0,"w":1,"h":1}""",
            [(small, """{"x":0.4,"y":0.4,"w":0.1,"h":0.1}""")]);

        result.Should().Equal(small);
    }
}
