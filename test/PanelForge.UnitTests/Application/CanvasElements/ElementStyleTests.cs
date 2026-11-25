using FluentAssertions;
using Moq;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.CanvasElements.Commands.UpdateElementStyle;
using PanelForge.Application.Features.CanvasElements.Queries.GetPageSnapshot;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;
using PanelForge.Domain.Events;
using PanelForge.UnitTests.Common;
using Xunit;

namespace PanelForge.UnitTests.Application.CanvasElements;

/// <summary>Thanh định dạng chữ của Letterer: kiểu chữ lưu bằng event (append-only), có kiểm hợp lệ và chống ghi đè.</summary>
public class ElementStyleTests
{
    private readonly Mock<IPageRepository> _repo = new();
    private readonly Guid _user = Guid.NewGuid();
    private readonly Page _page = Page.CreateNew(Guid.NewGuid(), 1, LayoutFormat.StandardPage, 1200, 1800, 300);
    private readonly Guid _balloon;
    private readonly Guid _artwork;

    private static readonly TextStyle Shout = new("Bangers", 40, Bold: true, StrokeColor: "#FFFFFF", StrokeWidth: 3, Preset: "Hét");

    public ElementStyleTests()
    {
        _page.AddElement("SPEECH_BALLOON", 10, 10, 300, 200, 1, _user, "Chạy đi!");
        _page.AddElement("ARTWORK_LAYER", 0, 0, 1200, 1800, 0, _user);
        _balloon = _page.Elements.Values.Single(e => e.ElementType == "SPEECH_BALLOON").ElementId;
        _artwork = _page.Elements.Values.Single(e => e.ElementType == "ARTWORK_LAYER").ElementId;
        _repo.Setup(r => r.GetAsync(_page.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_page);
    }

    private Task<PanelForge.Application.Common.Result<int>> Update(Guid elementId, TextStyle? style, int? expected = null) =>
        new UpdateElementStyleCommandHandler(_repo.Object, InMemoryDb.Create())
            .Handle(new UpdateElementStyleCommand(_page.Id, elementId, style, _user, expected), default);

    [Fact]
    public async Task Update_ValidStyle_AppendsEventKeepsPreviousAndShowsInSnapshot()
    {
        var version = _page.Elements[_balloon].Version;

        (await Update(_balloon, Shout, version)).IsSuccess.Should().BeTrue();

        var evt = _page.UncommittedEvents.OfType<ElementStyleUpdatedEvent>().Single();
        evt.Style.Should().Be(Shout);
        evt.PreviousStyle.Should().BeNull();
        _page.Elements[_balloon].Version.Should().Be(version + 1);

        var snap = await new GetPageSnapshotQueryHandler(_repo.Object, InMemoryDb.Create())
            .Handle(new GetPageSnapshotQuery(_page.Id), default);
        snap.Value!.Elements.Single(e => e.ElementId == _balloon).Style.Should().Be(Shout);
    }

    [Fact]
    public async Task Update_SameStyleTwice_DoesNotAppendSecondEvent_AndNullResetsToDefault()
    {
        await Update(_balloon, Shout);
        await Update(_balloon, Shout with { });
        _page.UncommittedEvents.OfType<ElementStyleUpdatedEvent>().Should().HaveCount(1);

        (await Update(_balloon, null)).IsSuccess.Should().BeTrue();
        _page.Elements[_balloon].Style.Should().BeNull();
        _page.UncommittedEvents.OfType<ElementStyleUpdatedEvent>().Last().PreviousStyle.Should().Be(Shout);
    }

    [Theory]
    [InlineData("Comic Sans MS", 30, "#000000")]  // font ngoài danh sách
    [InlineData("Mali", 10, "#000000")]           // nhỏ hơn mức tối thiểu khi in
    [InlineData("Mali", 30, "red")]               // màu không phải #RRGGBB
    public async Task Update_InvalidStyle_IsRejected(string font, int size, string color)
    {
        var r = await Update(_balloon, new TextStyle(font, size, Color: color));
        r.IsSuccess.Should().BeFalse();
        _page.Elements[_balloon].Style.Should().BeNull();
    }

    [Fact]
    public async Task Update_BalloonAppearance_IsStoredAndReturnedInSnapshot()
    {
        // "Chỉ chữ" trên bóng Artist đã vẽ: không hình bóng, chữ trắng viền đen, đuôi đã kéo
        var textOnly = new TextStyle("Mali", 26, Color: "#FFFFFF", StrokeColor: "#000000", StrokeWidth: 2,
            BalloonShape: "none", FillColor: "#FFFFFF", FillOpacity: 0, BorderColor: "#171717", BorderWidth: 0,
            TailX: -40, TailY: 260);

        (await Update(_balloon, textOnly)).IsSuccess.Should().BeTrue();

        var snap = await new GetPageSnapshotQueryHandler(_repo.Object, InMemoryDb.Create())
            .Handle(new GetPageSnapshotQuery(_page.Id), default);
        snap.Value!.Elements.Single(e => e.ElementId == _balloon).Style.Should().Be(textOnly);
    }

    [Theory]
    [InlineData("star", null, null, null)]   // hình bóng ngoài danh sách
    [InlineData("oval", 1.5, null, null)]    // độ đục > 1
    [InlineData("oval", null, 20.0, null)]   // viền quá dày
    [InlineData("oval", null, null, 9000.0)] // đuôi xa quá khổ trang
    public async Task Update_InvalidBalloonAppearance_IsRejected(string shape, double? opacity, double? border, double? tailX)
    {
        var r = await Update(_balloon, new TextStyle("Mali", 26, BalloonShape: shape, FillOpacity: opacity,
            BorderWidth: border, TailX: tailX));
        r.IsSuccess.Should().BeFalse();
        _page.Elements[_balloon].Style.Should().BeNull();
    }

    [Fact]
    public void OldStyleJson_WithoutBalloonFields_DeserializesWithDefaults()
    {
        // Event đã lưu trước khi có các trường bóng: không cần migration, các trường mới là null (= mặc định theo loại)
        var old = System.Text.Json.JsonSerializer.Deserialize<TextStyle>(
            """{"fontFamily":"Mali","fontSize":26,"color":"#171717","lineHeight":1.3}""",
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
        old.FontFamily.Should().Be("Mali");
        old.BalloonShape.Should().BeNull();
        old.FillOpacity.Should().BeNull();
        old.TailX.Should().BeNull();
        old.Validated().Should().Be(old);
    }

    [Fact]
    public async Task Update_NonTextElement_IsRejected()
    {
        (await Update(_artwork, Shout)).IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task Update_StaleElementVersion_IsConflict()
    {
        var r = await Update(_balloon, Shout, expected: 99);
        r.IsSuccess.Should().BeFalse();
        r.ErrorMessage.Should().Contain("Xung đột");
    }

    [Fact]
    public async Task Update_ClearsOverflowAcknowledgement()
    {
        _page.AcknowledgeOverflow(_balloon, "Câu dài có chủ ý", _user);
        (await Update(_balloon, Shout)).IsSuccess.Should().BeTrue();
        _page.Elements[_balloon].OverflowAckReason.Should().BeNull();
    }
}
