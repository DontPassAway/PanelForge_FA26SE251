using FluentAssertions;
using Moq;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.Chapters.Commands.FreezeScript;
using PanelForge.Application.Features.ScriptLines.Common;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Entities.Workspaces;
using PanelForge.Domain.Enums;
using PanelForge.Infrastructure.Persistence;
using PanelForge.UnitTests.Common;
using Xunit;
using DomainSeries = PanelForge.Domain.Entities.Content.Series;

namespace PanelForge.UnitTests.Application.Chapters;

/// <summary>D12: Writer nộp (Draft → InReview), chỉ Writer rút về, Producer Start Production (InReview → Frozen).</summary>
public class ScriptLifecycleTests
{
    private readonly PanelForgeDbContext _db = InMemoryDb.Create();
    private readonly Mock<IWorkspaceAuthorizationService> _auth = new();
    private readonly Guid _writer = Guid.NewGuid();
    private readonly Guid _editor = Guid.NewGuid();
    private readonly Guid _producer = Guid.NewGuid();
    private readonly StudioWorkspace _studio;
    private readonly Chapter _chapter;
    private readonly Scene _scene;

    public ScriptLifecycleTests()
    {
        _studio = StudioWorkspace.Create("Studio", _producer);
        _db.StudioWorkspaces.Add(_studio);
        var series = DomainSeries.Create(_studio.Id, "Lantern", ReadingDirection.RightToLeft);
        _db.Series.Add(series);
        _chapter = Chapter.Create(series.Id, 1, "Lá thư");
        _db.Chapters.Add(_chapter);
        _scene = Scene.Create(_chapter.Id, 1, "Bến cảng");
        _db.Scenes.Add(_scene);
        _db.SaveChanges();

        _auth.Setup(a => a.GetMemberRoleAsync(_writer, _studio.Id, It.IsAny<CancellationToken>())).ReturnsAsync(WorkspaceRole.Writer);
        _auth.Setup(a => a.GetMemberRoleAsync(_editor, _studio.Id, It.IsAny<CancellationToken>())).ReturnsAsync(WorkspaceRole.Editor);
        _auth.Setup(a => a.GetMemberRoleAsync(_producer, _studio.Id, It.IsAny<CancellationToken>())).ReturnsAsync(WorkspaceRole.Producer);
    }

    private ScriptLifecycleCommandHandler Lifecycle => new(_db, _auth.Object);

    private void AddLine()
    {
        _db.ScriptLines.Add(ScriptLine.Create(_scene.Id, 1, stageDirection: "Minh chạy ra bến cảng."));
        _db.SaveChanges();
    }

    [Fact]
    public void NewChapter_IsPreProduction_WithDraftScript()
    {
        _chapter.Status.Should().Be(ChapterStatus.PreProduction);
        _chapter.ScriptStatus.Should().Be(ScriptStatus.Draft);
        _chapter.ScriptLockReason.Should().BeNull();
    }

    [Fact]
    public async Task Writer_Submits_ScriptBecomesInReview_AndLocksEditing()
    {
        AddLine();

        var result = await Lifecycle.Handle(new SubmitScriptCommand(_chapter.Id, _writer), default);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value!.ScriptStatus.Should().Be(ScriptStatus.InReview);
        result.Value.ScriptSubmittedAt.Should().NotBeNull();

        var edit = await ScriptLineRules.AuthorizeAsync(_db, _auth.Object, _writer, _chapter.Id, null, default);
        edit.IsSuccess.Should().BeFalse("InReview khoá sửa kịch bản");
        edit.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
    }

    [Fact]
    public async Task Submit_EmptyScript_IsRejected()
    {
        var result = await Lifecycle.Handle(new SubmitScriptCommand(_chapter.Id, _writer), default);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ResultErrorCodes.Validation);
        _chapter.ScriptStatus.Should().Be(ScriptStatus.Draft);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OnlyWriter_CanSubmitOrRecall(bool asEditor)
    {
        AddLine();
        var other = asEditor ? _editor : _producer;

        var submit = await Lifecycle.Handle(new SubmitScriptCommand(_chapter.Id, other), default);
        submit.ErrorCode.Should().Be(ResultErrorCodes.Forbidden);

        await Lifecycle.Handle(new SubmitScriptCommand(_chapter.Id, _writer), default);
        var recall = await Lifecycle.Handle(new RecallScriptCommand(_chapter.Id, other), default);

        recall.ErrorCode.Should().Be(ResultErrorCodes.Forbidden, "Editor / Producer không đẩy kịch bản về Draft");
        _chapter.ScriptStatus.Should().Be(ScriptStatus.InReview);
    }

    [Fact]
    public async Task Writer_Recalls_BackToDraft_AndCanEditAgain()
    {
        AddLine();
        await Lifecycle.Handle(new SubmitScriptCommand(_chapter.Id, _writer), default);

        var result = await Lifecycle.Handle(new RecallScriptCommand(_chapter.Id, _writer), default);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value!.ScriptStatus.Should().Be(ScriptStatus.Draft);
        (await ScriptLineRules.AuthorizeAsync(_db, _auth.Object, _writer, _chapter.Id, null, default))
            .IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task StartProduction_RequiresSubmittedScript()
    {
        AddLine();

        var result = await new FreezeChapterScriptCommandHandler(_db)
            .Handle(new FreezeChapterScriptCommand(_chapter.Id, _producer), default);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        _chapter.Status.Should().Be(ChapterStatus.PreProduction);
    }

    [Fact]
    public async Task StartProduction_FreezesScript_AndMovesChapterIntoProduction_WithoutScaffoldingPages()
    {
        AddLine();
        await Lifecycle.Handle(new SubmitScriptCommand(_chapter.Id, _writer), default);

        var result = await new FreezeChapterScriptCommandHandler(_db)
            .Handle(new FreezeChapterScriptCommand(_chapter.Id, _producer), default);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value!.ScriptStatus.Should().Be(ScriptStatus.Frozen);
        result.Value.IsScriptFrozen.Should().BeTrue();
        result.Value.Status.Should().Be(ChapterStatus.InProduction);
        result.Value.ScriptContentHash.Should().MatchRegex("^[0-9a-f]{64}$");
        _db.Pages.Should().BeEmpty("khung Trang/Panel đến từ kịch bản, không tự dựng lúc freeze");
    }

    [Fact]
    public void Unfreeze_ReturnsScriptToDraft_ChapterStaysInProduction()
    {
        _chapter.SubmitScript(_writer);
        _chapter.StartProduction(_producer, "hash");

        _chapter.UnfreezeScript(_producer);

        _chapter.ScriptStatus.Should().Be(ScriptStatus.Draft);
        _chapter.IsScriptFrozen.Should().BeFalse();
        _chapter.ScriptContentHash.Should().BeNull();
        _chapter.Status.Should().Be(ChapterStatus.InProduction);
    }

    [Fact]
    public void Domain_RejectsInvalidTransitions()
    {
        FluentActions.Invoking(() => _chapter.RecallScript(_writer)).Should().Throw<InvalidOperationException>();
        FluentActions.Invoking(() => _chapter.StartProduction(_producer, null)).Should().Throw<InvalidOperationException>();

        _chapter.SubmitScript(_writer);
        FluentActions.Invoking(() => _chapter.SubmitScript(_writer)).Should().Throw<InvalidOperationException>();
    }
}
