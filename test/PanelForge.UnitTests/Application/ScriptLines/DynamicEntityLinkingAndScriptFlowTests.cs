using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using MediatR;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.Bible.Commands.CreateBibleEntry;
using PanelForge.Application.Features.Bible.Models;
using PanelForge.Application.Features.Chapters.Commands.FreezeScript;
using PanelForge.Application.Features.Chapters.Queries.CheckChapterBibleGate;
using PanelForge.Application.Features.Pages.Commands.TogglePageContentReady;
using PanelForge.Application.Features.ScriptLines.Commands.LinkBibleEntries;
using PanelForge.Application.Features.ScriptLines.Models;
using PanelForge.Domain.Entities.Bible;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Entities.Workspaces;
using PanelForge.Domain.Enums;
using PanelForge.Infrastructure.Persistence;
using PanelForge.UnitTests.Common;
using Xunit;
using DomainSeries = PanelForge.Domain.Entities.Content.Series;

namespace PanelForge.UnitTests.Application.ScriptLines;

/// <summary>
/// V2.5 Unit Tests: Dynamic Entity Linking, Stub Auto-Matching, Per-Page Content-Ready và Targeted Impact Scope (Phụ lục A DS01 & DS04).
/// </summary>
public class DynamicEntityLinkingAndScriptFlowTests
{
    private readonly PanelForgeDbContext _db = InMemoryDb.Create();
    private readonly Mock<IWorkspaceAuthorizationService> _auth = new();
    private readonly Mock<IMediator> _mediator = new();
    private readonly StudioWorkspace _studio = StudioWorkspace.Create("Studio", Guid.NewGuid());
    private readonly DomainSeries _series;
    private readonly Chapter _chapter;
    private readonly Scene _scene;
    private readonly Page _page;
    private readonly Panel _panel1;
    private readonly Panel _panel2;
    private readonly ScriptLine _line1;
    private readonly ScriptLine _line2;
    private readonly SeriesBible _bible;
    private readonly Guid _writer = Guid.NewGuid();
    private readonly Guid _producer = Guid.NewGuid();
    private readonly Guid _artist = Guid.NewGuid();

    public DynamicEntityLinkingAndScriptFlowTests()
    {
        _series = DomainSeries.Create(_studio.Id, "Manga V2.5", ReadingDirection.RightToLeft);
        _chapter = Chapter.Create(_series.Id, 1);
        _scene = Scene.Create(_chapter.Id, 1);
        _page = Page.CreateNew(_chapter.Id, 1, LayoutFormat.StandardPage, 1200, 1800, 300);
        _panel1 = Panel.Create(_page.Id, 1, 1, "0,0,600,900");
        _panel2 = Panel.Create(_page.Id, 2, 2, "600,0,600,900");

        _line1 = ScriptLine.Create(_scene.Id, 1, "Lời thoại panel 1", "Chỉ dẫn 1");
        _line1.AssignToPanel(_panel1.Id);
        _line2 = ScriptLine.Create(_scene.Id, 2, "Lời thoại panel 2", "Chỉ dẫn 2");
        _line2.AssignToPanel(_panel2.Id);

        _bible = SeriesBible.Create(_series.Id, _studio.Id, "Series Bible", "Mô tả");

        _db.StudioWorkspaces.Add(_studio);
        _db.Series.Add(_series);
        _db.Chapters.Add(_chapter);
        _db.Scenes.Add(_scene);
        _db.Pages.Add(_page);
        _db.Panels.AddRange(_panel1, _panel2);
        _db.ScriptLines.AddRange(_line1, _line2);
        _db.SeriesBibles.Add(_bible);
        _db.SaveChanges();

        _auth.Setup(a => a.GetMemberRoleAsync(_writer, _studio.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(WorkspaceRole.Writer);
        _auth.Setup(a => a.GetMemberRoleAsync(_producer, _studio.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(WorkspaceRole.Producer);
    }

    [Fact]
    public async Task LinkBibleEntries_WithRawText_CreatesStubEntityAndLinksSuccessfully()
    {
        // Arrange
        var handler = new LinkBibleEntriesCommandHandler(_db, _auth.Object);
        var mentions = new List<ScriptMentionItemDto>
        {
            new(BibleEntryId: null, RawText: "@Kuro", CategoryHint: BibleEntryCategory.Character)
        };

        // Act
        var result = await handler.Handle(
            new LinkBibleEntriesCommand(_writer, _chapter.Id, _line1.Id, mentions), default);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
        var linkDto = result.Value[0];
        linkDto.EntryName.Should().Be("Kuro");
        linkDto.IsStub.Should().BeTrue();
        linkDto.RawMentionText.Should().Be("@Kuro");

        var createdStub = await _db.BibleEntries.FirstOrDefaultAsync(e => e.Name == "Kuro");
        createdStub.Should().NotBeNull();
        createdStub!.IsStub.Should().BeTrue();
        createdStub.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task CreateBibleEntry_AutoMatching_RemapsExistingStubLinks()
    {
        // Arrange: Tạo trước 1 Stub Entity cho "Akira" và liên kết vào dòng 1
        var stub = BibleEntry.CreateStub(_bible.Id, BibleEntryCategory.Character, "Akira");
        _db.BibleEntries.Add(stub);
        var stubLink = ScriptLineBibleLink.Create(_line1.Id, stub.Id, "@Akira");
        _db.ScriptLineBibleLinks.Add(stubLink);
        await _db.SaveChangesAsync();

        var createHandler = new CreateBibleEntryCommandHandler(_db);
        var command = new CreateBibleEntryCommand(
            SeriesId: _series.Id,
            Category: BibleEntryCategory.Character,
            Code: "CHR_AKIRA",
            Name: "Akira",
            Description: "Nhân vật chính thức Akira",
            UserId: _producer
        );

        // Act: Tạo BibleEntry chính thức
        var createResult = await createHandler.Handle(command, default);

        // Assert
        createResult.IsSuccess.Should().BeTrue();
        var formalEntryId = createResult.Value!.Id;

        // Kiểm tra Stub Link đã được tự động chuyển hướng sang formal entry
        var updatedLink = await _db.ScriptLineBibleLinks.FirstAsync(l => l.ScriptLineId == _line1.Id);
        updatedLink.BibleEntryId.Should().Be(formalEntryId);

        // Stub cũ phải bị đánh dấu inactive
        var oldStub = await _db.BibleEntries.FirstAsync(e => e.Id == stub.Id);
        oldStub.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Page_ContentReady_Toggle_WorksForWriter()
    {
        // Arrange
        var markHandler = new MarkPageContentReadyCommandHandler(_db, _auth.Object);
        var unmarkHandler = new UnmarkPageContentReadyCommandHandler(_db, _auth.Object);

        // Act 1: Đánh dấu Content-Ready
        var markResult = await markHandler.Handle(
            new MarkPageContentReadyCommand(_chapter.Id, _page.Id, _writer), default);

        markResult.IsSuccess.Should().BeTrue();
        markResult.Value!.IsContentReady.Should().BeTrue();
        markResult.Value!.ContentReadyById.Should().Be(_writer);

        var pageDb = await _db.Pages.FirstAsync(p => p.Id == _page.Id);
        pageDb.IsContentReady.Should().BeTrue();
        pageDb.ContentReadyAt.Should().NotBeNull();

        // Act 2: Hủy cờ Content-Ready
        var unmarkResult = await unmarkHandler.Handle(
            new UnmarkPageContentReadyCommand(_chapter.Id, _page.Id, _writer), default);

        unmarkResult.IsSuccess.Should().BeTrue();
        unmarkResult.Value!.IsContentReady.Should().BeFalse();

        pageDb = await _db.Pages.FirstAsync(p => p.Id == _page.Id);
        pageDb.IsContentReady.Should().BeFalse();
    }

    [Fact]
    public async Task ScriptReopen_Governance_And_TargetedImpactAnalysis()
    {
        // Arrange: Tạo 2 assignments cho panel 1 và panel 2, cả hai đều Approved
        var stageId = Guid.NewGuid();
        var assignPanel1 = Assignment.Create(
            _series.Id, _chapter.Id, WorkflowEntityType.Panel, _panel1.Id, stageId, _artist, WorkspaceRole.Artist, "Vẽ panel 1");
        assignPanel1.MarkApproved();

        var assignPanel2 = Assignment.Create(
            _series.Id, _chapter.Id, WorkflowEntityType.Panel, _panel2.Id, stageId, _artist, WorkspaceRole.Artist, "Vẽ panel 2");
        assignPanel2.MarkApproved();

        _db.Assignments.AddRange(assignPanel1, assignPanel2);

        // Đóng băng chương ban đầu
        _chapter.FreezeScript(_producer);
        await _db.SaveChangesAsync();

        // 1. Writer gửi yêu cầu mở lại kịch bản
        var requestHandler = new RequestScriptReopenCommandHandler(_db);
        var reqResult = await requestHandler.Handle(
            new RequestScriptReopenCommand(_chapter.Id, _writer, "Cần sửa thoại Panel 2 do đổi twist"), default);

        reqResult.IsSuccess.Should().BeTrue();
        reqResult.Value!.ScriptReopenStatus.Should().Be(ScriptReopenStatus.Requested);
        reqResult.Value!.ScriptReopenReason.Should().Be("Cần sửa thoại Panel 2 do đổi twist");

        // 2. Producer duyệt mở kịch bản
        var approveHandler = new ApproveScriptReopenCommandHandler(_db);
        var approveResult = await approveHandler.Handle(
            new ApproveScriptReopenCommand(_chapter.Id, _producer), default);

        approveResult.IsSuccess.Should().BeTrue();
        approveResult.Value!.IsScriptFrozen.Should().BeFalse();
        approveResult.Value!.ScriptContentHash.Should().BeNull();
        approveResult.Value!.ScriptReopenStatus.Should().Be(ScriptReopenStatus.Approved);

        // 3. Giả lập Writer sửa thoại dòng 2 (gắn trên Panel 2)
        await Task.Delay(20);
        _line2.UpdateContent("Thoại mới bí ẩn cho Panel 2", "Chỉ dẫn mới", null);
        await _db.SaveChangesAsync();

        // 4. Producer đóng băng lại kịch bản (kích hoạt Targeted Impact Analysis)
        _mediator.Setup(m => m.Send(It.IsAny<PanelForge.Application.Features.Chapters.Queries.CheckChapterBibleGate.CheckChapterBibleGateQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ChapterBibleGateResultDto>.Success(new ChapterBibleGateResultDto(
                IsPassed: true, TotalReferenced: 0, LockedCount: 0, PendingCount: 0, Items: [])));

        var freezeHandler = new FreezeChapterScriptCommandHandler(_db, _mediator.Object);
        var freezeResult = await freezeHandler.Handle(
            new FreezeChapterScriptCommand(_chapter.Id, _producer), default);

        freezeResult.IsSuccess.Should().BeTrue();
        freezeResult.Value!.IsScriptFrozen.Should().BeTrue();
        freezeResult.Value!.ScriptContentHash.Should().MatchRegex("^[0-9a-f]{64}$");

        // 5. Kiểm tra Targeted Impact Scope:
        // Panel 2 bị đổi thoại => Chuyển sang ReworkRequired và bật has_script_impact = true
        var updatedAssign2 = await _db.Assignments.FirstAsync(a => a.Id == assignPanel2.Id);
        updatedAssign2.HasScriptImpact.Should().BeTrue();
        updatedAssign2.Status.Should().Be(AssignmentStatus.ReworkRequired);
        updatedAssign2.ImpactedScriptLineId.Should().Be(_line2.Id);

        // Panel 1 không bị đổi thoại => Giữ nguyên 100% trạng thái Approved!
        var updatedAssign1 = await _db.Assignments.FirstAsync(a => a.Id == assignPanel1.Id);
        updatedAssign1.HasScriptImpact.Should().BeFalse();
        updatedAssign1.Status.Should().Be(AssignmentStatus.Approved);
    }
}

