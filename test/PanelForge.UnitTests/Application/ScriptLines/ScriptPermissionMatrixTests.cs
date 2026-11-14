using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.Chapters.Commands.FreezeScript;
using PanelForge.Application.Features.Pages.Commands.SyncScriptStructure;
using PanelForge.Application.Features.ScriptLines.Common;
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
/// SCR-11 / BR-07 / Q24: Ma trận phân quyền sửa kịch bản theo vai trò × trạng thái ScriptStatus.
/// - Producer chỉ đọc kịch bản (403 Forbidden); không được sửa, lưu, xoá dòng hay cấu trúc.
/// - Chỉ Writer mới có quyền sửa/commit kịch bản (khi chưa đóng băng).
/// - Artist, Letterer, Editor luôn bị từ chối 403 ngay cả khi có thẻ assignment.
/// - Producer vẫn có quyền Bắt đầu sản xuất (Freeze), Mở lại (Unfreeze), và duyệt cấp chương.
/// </summary>
public class ScriptPermissionMatrixTests
{
    private readonly PanelForgeDbContext _db = InMemoryDb.Create();
    private readonly Mock<IWorkspaceAuthorizationService> _auth = new();
    private readonly Mock<IPageRepository> _pages = new();
    private readonly StudioWorkspace _studio;
    private readonly DomainSeries _series;
    private readonly Chapter _chapter;
    private readonly Scene _scene;

    private readonly Guid _producerId = Guid.NewGuid();
    private readonly Guid _writerId = Guid.NewGuid();
    private readonly Guid _editorId = Guid.NewGuid();
    private readonly Guid _artistId = Guid.NewGuid();
    private readonly Guid _lettererId = Guid.NewGuid();
    private readonly Guid _otherWriterId = Guid.NewGuid();
    private readonly Guid _nonMemberId = Guid.NewGuid();

    public ScriptPermissionMatrixTests()
    {
        _studio = StudioWorkspace.Create("Studio Matrix", _producerId);
        _db.StudioWorkspaces.Add(_studio);

        _series = DomainSeries.Create(_studio.Id, "Permission Chronicles", ReadingDirection.RightToLeft);
        _db.Series.Add(_series);

        _chapter = Chapter.Create(_series.Id, 1, "Chương 1");
        _db.Chapters.Add(_chapter);

        _scene = Scene.Create(_chapter.Id, 1, "Cảnh mở đầu");
        _db.Scenes.Add(_scene);

        var line = ScriptLine.Create(_scene.Id, 1, stageDirection: "Mặt trời mọc.");
        _db.ScriptLines.Add(line);

        _db.SaveChanges();

        _auth.Setup(a => a.GetMemberRoleAsync(_producerId, _studio.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(WorkspaceRole.Producer);
        _auth.Setup(a => a.GetMemberRoleAsync(_writerId, _studio.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(WorkspaceRole.Writer);
        _auth.Setup(a => a.GetMemberRoleAsync(_otherWriterId, _studio.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(WorkspaceRole.Writer);
        _auth.Setup(a => a.GetMemberRoleAsync(_editorId, _studio.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(WorkspaceRole.Editor);
        _auth.Setup(a => a.GetMemberRoleAsync(_artistId, _studio.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(WorkspaceRole.Artist);
        _auth.Setup(a => a.GetMemberRoleAsync(_lettererId, _studio.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(WorkspaceRole.Letterer);
        _auth.Setup(a => a.GetMemberRoleAsync(_nonMemberId, _studio.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkspaceRole?)null);
    }

    private void SetScriptStatus(ScriptStatus status)
    {
        switch (status)
        {
            case ScriptStatus.Draft:
                // Trạng thái mặc định ban đầu
                break;
            case ScriptStatus.InReview:
                _chapter.SubmitScript(_writerId);
                break;
            case ScriptStatus.Frozen:
                _chapter.SubmitScript(_writerId);
                _chapter.StartProduction(_producerId, "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef");
                break;
        }
        _db.SaveChanges();
    }

    [Theory]
    [InlineData(WorkspaceRole.Producer, ScriptStatus.Draft)]
    [InlineData(WorkspaceRole.Producer, ScriptStatus.InReview)]
    [InlineData(WorkspaceRole.Producer, ScriptStatus.Frozen)]
    [InlineData(WorkspaceRole.Editor, ScriptStatus.Draft)]
    [InlineData(WorkspaceRole.Editor, ScriptStatus.InReview)]
    [InlineData(WorkspaceRole.Editor, ScriptStatus.Frozen)]
    [InlineData(WorkspaceRole.Artist, ScriptStatus.Draft)]
    [InlineData(WorkspaceRole.Artist, ScriptStatus.InReview)]
    [InlineData(WorkspaceRole.Artist, ScriptStatus.Frozen)]
    [InlineData(WorkspaceRole.Letterer, ScriptStatus.Draft)]
    [InlineData(WorkspaceRole.Letterer, ScriptStatus.InReview)]
    [InlineData(WorkspaceRole.Letterer, ScriptStatus.Frozen)]
    public async Task NonWriterRoles_Always403_Forbidden_AcrossAllScriptStatuses(WorkspaceRole role, ScriptStatus status)
    {
        SetScriptStatus(status);

        var userId = role switch
        {
            WorkspaceRole.Producer => _producerId,
            WorkspaceRole.Editor => _editorId,
            WorkspaceRole.Artist => _artistId,
            WorkspaceRole.Letterer => _lettererId,
            _ => throw new ArgumentOutOfRangeException(nameof(role))
        };

        var result = await ScriptLineRules.AuthorizeAsync(_db, _auth.Object, userId, _chapter.Id, null, default);

        result.IsSuccess.Should().BeFalse($"vai trò {role} không được phép sửa kịch bản ở trạng thái {status}");
        result.ErrorCode.Should().Be(ResultErrorCodes.Forbidden);

        if (role == WorkspaceRole.Producer)
        {
            result.ErrorMessage.Should().Contain("Producer chỉ có quyền xem");
        }
    }

    [Theory]
    [InlineData(WorkspaceRole.Artist)]
    [InlineData(WorkspaceRole.Letterer)]
    [InlineData(WorkspaceRole.Editor)]
    public async Task NonWriterRoles_WithActiveAssignments_Still403_Forbidden(WorkspaceRole role)
    {
        SetScriptStatus(ScriptStatus.Draft);

        var userId = role switch
        {
            WorkspaceRole.Artist => _artistId,
            WorkspaceRole.Letterer => _lettererId,
            WorkspaceRole.Editor => _editorId,
            _ => throw new ArgumentOutOfRangeException(nameof(role))
        };

        // Gán assignment cho vai trò này
        var assignment = Assignment.Create(
            _series.Id,
            _chapter.Id,
            WorkflowEntityType.Chapter,
            _chapter.Id,
            _producerId,
            userId,
            role,
            "Nhiệm vụ sản xuất");
        _db.Assignments.Add(assignment);
        await _db.SaveChangesAsync();

        var result = await ScriptLineRules.AuthorizeAsync(_db, _auth.Object, userId, _chapter.Id, null, default);

        result.IsSuccess.Should().BeFalse("kể cả khi có assignment thì chỉ Writer mới được sửa kịch bản");
        result.ErrorCode.Should().Be(ResultErrorCodes.Forbidden);
    }

    [Fact]
    public async Task Writer_InDraftStatus_Succeeds()
    {
        SetScriptStatus(ScriptStatus.Draft);

        var result = await ScriptLineRules.AuthorizeAsync(_db, _auth.Object, _writerId, _chapter.Id, null, default);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
    }

    [Fact]
    public async Task Writer_InReviewStatus_CanKeepEditingAndCommitting()
    {
        SetScriptStatus(ScriptStatus.InReview);

        // Theo D12 & Q15: Editor đọc bản đã gửi (push), Writer vẫn được sửa tiếp và gửi lại
        var result = await ScriptLineRules.AuthorizeAsync(_db, _auth.Object, _writerId, _chapter.Id, null, default);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
    }

    [Fact]
    public async Task Writer_InFrozenStatus_IsBlockedWithConflict_409()
    {
        SetScriptStatus(ScriptStatus.Frozen);

        var result = await ScriptLineRules.AuthorizeAsync(_db, _auth.Object, _writerId, _chapter.Id, null, default);

        result.IsSuccess.Should().BeFalse("kịch bản đóng băng khoá mọi thao tác sửa");
        result.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        result.ErrorMessage.Should().Contain("Kịch bản đã được chốt để sản xuất hình ảnh");
    }

    [Fact]
    public async Task Writer_WhenScriptAssignedToOtherWriter_Returns403_Forbidden()
    {
        SetScriptStatus(ScriptStatus.Draft);

        // Giao việc kịch bản cho _writerId
        var assignment = Assignment.Create(
            _series.Id,
            _chapter.Id,
            WorkflowEntityType.Chapter,
            _chapter.Id,
            _producerId,
            _writerId,
            WorkspaceRole.Writer,
            "Viết kịch bản");
        _db.Assignments.Add(assignment);
        await _db.SaveChangesAsync();

        // _otherWriterId cố gắng sửa
        var result = await ScriptLineRules.AuthorizeAsync(_db, _auth.Object, _otherWriterId, _chapter.Id, null, default);

        result.IsSuccess.Should().BeFalse("chỉ Writer được giao mới có quyền sửa khi chương đã giao kịch bản");
        result.ErrorCode.Should().Be(ResultErrorCodes.Forbidden);
        result.ErrorMessage.Should().Contain("đã được giao cho Writer khác");
    }

    [Fact]
    public async Task NonMember_Returns403_Forbidden()
    {
        SetScriptStatus(ScriptStatus.Draft);

        var result = await ScriptLineRules.AuthorizeAsync(_db, _auth.Object, _nonMemberId, _chapter.Id, null, default);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ResultErrorCodes.Forbidden);
        result.ErrorMessage.Should().Contain("Bạn không phải thành viên");
    }

    [Fact]
    public async Task Producer_CanStill_StartProduction_And_ReopenScript()
    {
        // 1. Writer nộp kịch bản
        _chapter.SubmitScript(_writerId);
        await _db.SaveChangesAsync();

        // 2. Producer Start Production (Freeze Script)
        var freezeResult = await new FreezeChapterScriptCommandHandler(_db)
            .Handle(new FreezeChapterScriptCommand(_chapter.Id, _producerId), default);

        freezeResult.IsSuccess.Should().BeTrue(freezeResult.ErrorMessage);
        _chapter.ScriptStatus.Should().Be(ScriptStatus.Frozen);
        _chapter.Status.Should().Be(ChapterStatus.InProduction);

        // 3. Producer Mở lại kịch bản (Unfreeze)
        var unfreezeResult = await new UnfreezeChapterScriptCommandHandler(_db)
            .Handle(new UnfreezeChapterScriptCommand(_chapter.Id, _producerId, "Cần sửa bổ sung thoại nhân vật"), default);

        unfreezeResult.IsSuccess.Should().BeTrue(unfreezeResult.ErrorMessage);
        _chapter.ScriptStatus.Should().Be(ScriptStatus.Draft);
        _chapter.Status.Should().Be(ChapterStatus.InProduction);
    }

    [Fact]
    public async Task Producer_CannotSyncScriptStructure_Returns403_Forbidden()
    {
        SetScriptStatus(ScriptStatus.Draft);

        var handler = new SyncScriptStructureCommandHandler(
            _db,
            _pages.Object,
            new PassThroughUnitOfWork(),
            _auth.Object);

        var result = await handler.Handle(
            new SyncScriptStructureCommand(_chapter.Id, _producerId, [new ScriptStructurePage(1, [])]),
            default);

        result.IsSuccess.Should().BeFalse("Producer không được phép tạo/sửa khung trang kịch bản");
        result.ErrorCode.Should().Be(ResultErrorCodes.Forbidden);
    }
}
