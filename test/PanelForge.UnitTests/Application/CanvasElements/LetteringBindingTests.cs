using FluentAssertions;
using Moq;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.CanvasElements.Commands.AddElement;
using PanelForge.Application.Features.CanvasElements.Queries.GetPageSnapshot;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;
using PanelForge.Infrastructure.Persistence;
using PanelForge.UnitTests.Common;
using Xunit;

namespace PanelForge.UnitTests.Application.CanvasElements;

/// <summary>BR-05: một ScriptLine gắn tối đa một text element; phát hiện chữ lỗi thời khi Writer sửa thoại.</summary>
public class LetteringBindingTests
{
    private readonly PanelForgeDbContext _db = InMemoryDb.Create();
    private readonly Mock<IPageRepository> _repo = new();
    private readonly Guid _chapterId = Guid.NewGuid();
    private readonly Guid _speakerId = Guid.NewGuid();
    private readonly Page _page;

    public LetteringBindingTests()
    {
        _page = Page.CreateNew(_chapterId, 1, LayoutFormat.StandardPage, 1000, 1500, 300);
        _repo.Setup(r => r.GetAsync(_page.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_page);
    }

    private async Task<ScriptLine> SeedLineAsync(Guid? chapterId = null, string text = "Đợi tôi với!")
    {
        var scene = Scene.Create(chapterId ?? _chapterId, 1);
        var line = ScriptLine.Create(scene.Id, 1, text, speakerCharacterId: _speakerId);
        _db.Scenes.Add(scene);
        _db.ScriptLines.Add(line);
        await _db.SaveChangesAsync();
        return line;
    }

    private Task<PanelForge.Application.Common.Result<AddElementResponse>> BindAsync(
        Guid lineId, string type = "SPEECH_BALLOON", string? content = null) =>
        new AddElementCommandHandler(_repo.Object, _db).Handle(
            new AddElementCommand(_page.Id, type, 0, 0, 100, 50, 1, content, null, Guid.NewGuid(), lineId),
            CancellationToken.None);

    [Fact]
    public async Task Bind_TakesSpeakerAndTextFromLine()
    {
        var line = await SeedLineAsync();

        var result = await BindAsync(line.Id);

        result.IsSuccess.Should().BeTrue();
        result.Value!.SpeakerId.Should().Be(_speakerId);
        var el = _page.Elements[result.Value.ElementId];
        el.ScriptLineId.Should().Be(line.Id);
        el.RenderedText.Should().Be("Đợi tôi với!");
        el.SourceLineText.Should().Be("Đợi tôi với!");
    }

    [Fact]
    public async Task Bind_SameLineTwice_IsBlocked_ButAllowedAfterRemove()
    {
        var line = await SeedLineAsync();
        var first = await BindAsync(line.Id);

        var second = await BindAsync(line.Id);
        second.IsSuccess.Should().BeFalse();
        second.ErrorMessage.Should().Contain(Page.DuplicateBindingCode);

        _page.RemoveElement(first.Value!.ElementId, "test", Guid.NewGuid());
        (await BindAsync(line.Id)).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Bind_RejectsMissingLine_OtherChapter_AndNonTextElement()
    {
        (await BindAsync(Guid.NewGuid())).ErrorMessage.Should().Contain("không tồn tại");

        var foreign = await SeedLineAsync(chapterId: Guid.NewGuid());
        (await BindAsync(foreign.Id)).IsSuccess.Should().BeFalse();

        var line = await SeedLineAsync();
        (await BindAsync(line.Id, type: "ARTWORK_LAYER")).IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task Snapshot_FlagsStale_WhenWriterEditsLine()
    {
        var line = await SeedLineAsync();
        await BindAsync(line.Id, content: "Đợi tôi!"); // Letterer rút gọn: Content ≠ thoại gốc nhưng chưa lỗi thời
        var handler = new GetPageSnapshotQueryHandler(_repo.Object, _db);

        var before = await handler.Handle(new GetPageSnapshotQuery(_page.Id), CancellationToken.None);
        before.Value!.Elements.Single().IsStale.Should().BeFalse();

        line.UpdateContent("Đợi tôi một chút!", null, _speakerId);
        await _db.SaveChangesAsync();

        var after = (await handler.Handle(new GetPageSnapshotQuery(_page.Id), CancellationToken.None)).Value!.Elements.Single();
        after.IsStale.Should().BeTrue();
        after.CurrentLineText.Should().Be("Đợi tôi một chút!");
        after.RenderedText.Should().Be("Đợi tôi!");
    }
}
