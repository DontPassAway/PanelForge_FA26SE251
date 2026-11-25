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
}
