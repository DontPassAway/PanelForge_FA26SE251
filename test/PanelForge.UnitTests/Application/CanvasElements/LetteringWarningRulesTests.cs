using FluentAssertions;
using PanelForge.Application.Features.CanvasElements.Common;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;
using Xunit;

namespace PanelForge.UnitTests.Application.CanvasElements;

/// <summary>BR-20: cảnh báo bằng code — tràn chữ, sai thứ tự đọc, đè Key Artwork Region.</summary>
public class LetteringWarningRulesTests
{
    private static readonly IReadOnlyDictionary<Guid, (int, int)> NoLines = new Dictionary<Guid, (int, int)>();

    private static ElementState Text(double x, double y, double w = 200, double h = 100, string? text = "Hi", Guid? line = null) => new()
    {
        ElementId = Guid.NewGuid(), ElementType = "SPEECH_BALLOON", X = x, Y = y, Width = w, Height = h,
        Content = text, RenderedText = text, ScriptLineId = line
    };

    private static ElementState Region(double x, double y, double w, double h) => new()
    {
        ElementId = Guid.NewGuid(), ElementType = LetteringWarningRules.KeyRegionType, X = x, Y = y, Width = w, Height = h
    };

    [Fact]
    public void Overflow_WhenTextLongerThanEstimatedCapacity()
    {
        var small = Text(0, 0, 80, 60, new string('a', 200));
        var fits = Text(300, 0, 400, 300, "ngắn");

        var w = LetteringWarningRules.Evaluate([small, fits], ReadingDirection.LeftToRight, NoLines);

        w.Should().ContainSingle(x => x.Code == LetteringWarningRules.TextOverflow && x.ElementId == small.ElementId);
    }

    [Fact]
    public void KeyRegion_FlagsOnlyOverlappingTextElements()
    {
        var region = Region(100, 100, 100, 100);
        var over = Text(150, 150);
        var clear = Text(500, 500);

        var w = LetteringWarningRules.Evaluate([region, over, clear], ReadingDirection.LeftToRight, NoLines);

        w.Should().ContainSingle(x => x.Code == LetteringWarningRules.OverlapsKeyRegion);
        w.Single().ElementId.Should().Be(over.ElementId);
        w.Single().RelatedElementId.Should().Be(region.ElementId);
    }

    [Theory]
    [InlineData(ReadingDirection.RightToLeft, true)]   // dòng 1 bên trái, dòng 2 bên phải: sai với RTL
    [InlineData(ReadingDirection.LeftToRight, false)]
    public void ReadingOrder_FollowsSeriesDirection_WithinSameRow(ReadingDirection dir, bool expectWarning)
    {
        var l1 = Guid.NewGuid();
        var l2 = Guid.NewGuid();
        var first = Text(50, 100, line: l1);
        var second = Text(500, 105, line: l2);
        var order = new Dictionary<Guid, (int, int)> { [l1] = (1, 1), [l2] = (1, 2) };

        var w = LetteringWarningRules.Evaluate([first, second], dir, order);

        w.Any(x => x.Code == LetteringWarningRules.ReadingOrderMismatch).Should().Be(expectWarning);
    }

    [Fact]
    public void ReadingOrder_LowerRowComesLater_RegardlessOfDirection()
    {
        var l1 = Guid.NewGuid();
        var l2 = Guid.NewGuid();
        var top = Text(50, 50, line: l1);
        var bottom = Text(50, 800, line: l2);
        var order = new Dictionary<Guid, (int, int)> { [l1] = (1, 2), [l2] = (1, 1) }; // dòng dưới lại là dòng đầu script

        var w = LetteringWarningRules.Evaluate([top, bottom], ReadingDirection.RightToLeft, order);

        w.Should().ContainSingle(x => x.Code == LetteringWarningRules.ReadingOrderMismatch && x.ElementId == top.ElementId);
    }
}
