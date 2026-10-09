using FluentAssertions;
using Moq;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.CanvasElements.Commands.AcknowledgeOverflow;
using PanelForge.Application.Features.CanvasElements.Commands.AddElement;
using PanelForge.Application.Features.CanvasElements.Commands.SetReadingOrder;
using PanelForge.Application.Features.CanvasElements.Commands.UpdateElementText;
using PanelForge.Application.Features.CanvasElements.Common;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;
using PanelForge.Infrastructure.Persistence;
using PanelForge.UnitTests.Common;
using Xunit;

namespace PanelForge.UnitTests.Application.CanvasElements;

/// <summary>Khép kín BR-05 / BR-20: sửa chữ, gắn lại dòng, xác nhận tràn chữ, thứ tự đọc, cổng nộp Lettering.</summary>
public class LetteringClosureTests
{
    private readonly PanelForgeDbContext _db = InMemoryDb.Create();
    private readonly Mock<IPageRepository> _repo = new();
    private readonly Guid _chapterId = Guid.NewGuid();
    private readonly Guid _user = Guid.NewGuid();
    private readonly Page _page;

    public LetteringClosureTests()
    {
        _page = Page.CreateNew(_chapterId, 1, LayoutFormat.StandardPage, 1000, 1500, 300);
        _repo.Setup(r => r.GetAsync(_page.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_page);
    }

    private async Task<ScriptLine> SeedLineAsync(int order = 1, string text = "Đợi tôi với!")
    {
        var scene = Scene.Create(_chapterId, order);
        var line = ScriptLine.Create(scene.Id, 1, text);
        _db.Scenes.Add(scene);
        _db.ScriptLines.Add(line);
        await _db.SaveChangesAsync();
        return line;
    }

    private async Task<Guid> AddAsync(Guid? lineId = null, string? content = null, double w = 200, double h = 100)
    {
        var r = await new AddElementCommandHandler(_repo.Object, _db).Handle(
            new AddElementCommand(_page.Id, "SPEECH_BALLOON", 0, 0, w, h, 1, content, null, _user, lineId), default);
        r.IsSuccess.Should().BeTrue(r.ErrorMessage);
        return r.Value!.ElementId;
    }

    private Task<PageLetteringAnalysis> AnalyzeAsync() => LetteringPageAnalysis.AnalyzeAsync(_db, _page, default);

    [Fact]
    public async Task UpdateText_RefreshesStaleElement_AndSubmissionGateReopens()
    {
        var line = await SeedLineAsync();
        var el = await AddAsync(line.Id);

        line.UpdateContent("Đợi tôi một chút!", null, null);
        await _db.SaveChangesAsync();

        var stale = await AnalyzeAsync();
        stale.StaleElementIds.Should().Contain(el);
        stale.SubmissionBlockReason(1).Should().Contain("lỗi thời");

        var result = await new UpdateElementTextCommandHandler(_repo.Object, _db)
            .Handle(new UpdateElementTextCommand(_page.Id, el, null, null, _user), default);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        _page.Elements[el].RenderedText.Should().Be("Đợi tôi một chút!");
        var fresh = await AnalyzeAsync();
        fresh.StaleElementIds.Should().BeEmpty();
        fresh.SubmissionBlockReason(1).Should().BeNull();
    }

    [Fact]
    public async Task UpdateText_Rebind_RespectsUniqueness_AndFreesOldLine()
    {
        var l1 = await SeedLineAsync(1, "Một");
        var l2 = await SeedLineAsync(2, "Hai");
        await AddAsync(l1.Id);
        var e2 = await AddAsync(l2.Id);
        var handler = new UpdateElementTextCommandHandler(_repo.Object, _db);

        var dup = await handler.Handle(new UpdateElementTextCommand(_page.Id, e2, null, l1.Id, _user), default);
        dup.IsSuccess.Should().BeFalse();
        dup.ErrorMessage.Should().Contain(Page.DuplicateBindingCode);

        var l3 = await SeedLineAsync(3, "Ba");
        (await handler.Handle(new UpdateElementTextCommand(_page.Id, e2, null, l3.Id, _user), default)).IsSuccess.Should().BeTrue();
        _page.Elements[e2].ScriptLineId.Should().Be(l3.Id);
        _page.Elements[e2].RenderedText.Should().Be("Ba");

        // Dòng 2 đã được nhả: gắn element khác được
        (await AddAsync(l2.Id)).Should().NotBeEmpty();
    }

    [Fact]
    public async Task OverflowAck_NeedsRealWarning_AndReason_AndUnlocksGate_UntilResized()
    {
        var el = await AddAsync(content: new string('a', 200), w: 80, h: 60);
        var ack = new AcknowledgeOverflowCommandHandler(_repo.Object, _db);

        (await AnalyzeAsync()).SubmissionBlockReason(1).Should().Contain("tràn chữ");
        (await ack.Handle(new AcknowledgeOverflowCommand(_page.Id, el, "  ", _user), default)).IsSuccess.Should().BeFalse();

        (await ack.Handle(new AcknowledgeOverflowCommand(_page.Id, el, "Chữ nhỏ có chủ ý theo storyboard", _user), default))
            .IsSuccess.Should().BeTrue();
        var acked = await AnalyzeAsync();
        acked.Warnings.Single(w => w.Code == LetteringWarningRules.TextOverflow).Acknowledged.Should().BeTrue();
        acked.SubmissionBlockReason(1).Should().BeNull();

        // Đổi kích thước hộp thì xác nhận cũ hết hiệu lực
        _page.MoveElement(el, 0, 0, 90, 60, _user);
        (await AnalyzeAsync()).SubmissionBlockReason(1).Should().Contain("tràn chữ");

        // Không có cảnh báo thì không xác nhận được
        var fine = await AddAsync(content: "ngắn", w: 400, h: 300);
        (await ack.Handle(new AcknowledgeOverflowCommand(_page.Id, fine, "x", _user), default)).IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task ReadingOrderOverride_ReplacesSpatialInference()
    {
        var l1 = await SeedLineAsync(1, "Một");
        var l2 = await SeedLineAsync(2, "Hai");
        var first = await AddAsync(l1.Id);
        var second = await AddAsync(l2.Id);
        _page.MoveElement(first, 600, 100, 200, 100, _user);  // LTR: dòng 1 nằm bên phải dòng 2 → sai
        _page.MoveElement(second, 50, 100, 200, 100, _user);

        (await AnalyzeAsync()).Warnings.Should().Contain(w => w.Code == LetteringWarningRules.ReadingOrderMismatch);

        var set = new SetReadingOrderCommandHandler(_repo.Object);
        (await set.Handle(new SetReadingOrderCommand(_page.Id, first, 1, _user), default)).IsSuccess.Should().BeTrue();
        (await set.Handle(new SetReadingOrderCommand(_page.Id, second, 2, _user), default)).IsSuccess.Should().BeTrue();

        (await AnalyzeAsync()).Warnings.Should().NotContain(w => w.Code == LetteringWarningRules.ReadingOrderMismatch);
        (await set.Handle(new SetReadingOrderCommand(_page.Id, first, 0, _user), default)).IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task ConcurrentPageUpdate_IsReportedAsConflict_NotThrown()
    {
        var line = await SeedLineAsync();
        _repo.Setup(r => r.SaveAsync(It.IsAny<Page>(), It.IsAny<CancellationToken>()))
             .ThrowsAsync(new ConcurrentPageUpdateException(_page.Id));

        var r = await new AddElementCommandHandler(_repo.Object, _db).Handle(
            new AddElementCommand(_page.Id, "SPEECH_BALLOON", 0, 0, 200, 100, 1, null, null, _user, line.Id), default);

        r.IsSuccess.Should().BeFalse();
        r.ErrorMessage.Should().Contain("Xung đột");
    }
}
