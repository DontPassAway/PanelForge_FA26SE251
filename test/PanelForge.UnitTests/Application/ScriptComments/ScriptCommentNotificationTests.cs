using FluentAssertions;
using Moq;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.ScriptComments;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Entities.Identity;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Entities.Workspaces;
using PanelForge.Domain.Enums;
using PanelForge.UnitTests.Common;
using Xunit;

namespace PanelForge.UnitTests.Application.ScriptComments;

/// <summary>D12: góp ý kịch bản báo Writer của Series (trừ người viết) bằng loại thông báo riêng ScriptCommentAdded.</summary>
public class ScriptCommentNotificationTests
{
    private readonly Guid _workspaceId = Guid.NewGuid();
    private readonly Guid _editorId = Guid.NewGuid();
    private readonly Guid _writerId = Guid.NewGuid();
    private readonly Guid _producerId = Guid.NewGuid();
    private readonly Series _series;
    private readonly Chapter _chapter;
    private readonly List<Notification> _notifications = [];
    private readonly List<ScriptComment> _comments = [];
    private readonly Mock<IPanelForgeDbContext> _db = new();
    private readonly Mock<IWorkspaceAuthorizationService> _auth = new();

    public ScriptCommentNotificationTests()
    {
        _series = Series.Create(_workspaceId, "Series", ReadingDirection.RightToLeft);
        _chapter = Chapter.Create(_series.Id, 1m);

        var seriesMembers = new List<SeriesMember>
        {
            SeriesMember.Create(_series.Id, _editorId, WorkspaceRole.Editor),
            SeriesMember.Create(_series.Id, _writerId, WorkspaceRole.Writer),
        };
        var workspaceMembers = new List<WorkspaceMember>
        {
            WorkspaceMember.Create(_workspaceId, _editorId, WorkspaceRole.Editor),
            WorkspaceMember.Create(_workspaceId, _writerId, WorkspaceRole.Writer),
            WorkspaceMember.Create(_workspaceId, _producerId, WorkspaceRole.Producer),
        };

        _db.Setup(d => d.Series).Returns(DbSetMockHelper.CreateDbSetMock(new List<Series> { _series }));
        _db.Setup(d => d.Chapters).Returns(DbSetMockHelper.CreateDbSetMock(new List<Chapter> { _chapter }));
        _db.Setup(d => d.SeriesMembers).Returns(DbSetMockHelper.CreateDbSetMock(seriesMembers));
        _db.Setup(d => d.WorkspaceMembers).Returns(DbSetMockHelper.CreateDbSetMock(workspaceMembers));
        _db.Setup(d => d.ScriptComments).Returns(DbSetMockHelper.CreateDbSetMock(_comments));
        _db.Setup(d => d.Users).Returns(DbSetMockHelper.CreateDbSetMock(new List<User>()));
        _db.Setup(d => d.Notifications).Returns(DbSetMockHelper.CreateDbSetMock(_notifications));
        _db.Setup(d => d.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        _auth.Setup(a => a.GetSeriesMemberRoleAsync(_editorId, _series.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(WorkspaceRole.Editor);
        _auth.Setup(a => a.GetSeriesMemberRoleAsync(_writerId, _series.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(WorkspaceRole.Writer);
    }

    [Fact]
    public async Task AddScriptComment_NotifiesWritersOnly_WithDedicatedType()
    {
        var handler = new ScriptCommentHandlers(_db.Object, _auth.Object);
        var cmd = new AddScriptCommentCommand(_chapter.Id, _editorId, null, null, null, "Lời thoại dòng 3 cần ngắn hơn");

        var result = await handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _notifications.Should().ContainSingle(n =>
            n.UserId == _writerId && n.Type == NotificationType.ScriptCommentAdded && n.ChapterId == _chapter.Id);
    }

    [Fact]
    public async Task AddScriptComment_ByWriter_IsForbidden_AndNotifiesNobody()
    {
        var handler = new ScriptCommentHandlers(_db.Object, _auth.Object);
        var cmd = new AddScriptCommentCommand(_chapter.Id, _writerId, null, null, null, "Tự góp ý");

        var result = await handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ResultErrorCodes.Forbidden);
        _notifications.Should().BeEmpty();
    }
}
