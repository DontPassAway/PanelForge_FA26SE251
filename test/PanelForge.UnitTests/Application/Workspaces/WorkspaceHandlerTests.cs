using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Workspaces.Commands.AddMember;
using PanelForge.Application.Features.Workspaces.Commands.CreateWorkspace;
using PanelForge.Application.Features.Workspaces.Commands.RemoveMember;
using PanelForge.Application.Features.Workspaces.Queries.GetMyWorkspaces;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Domain.Entities.Identity;
using PanelForge.Domain.Entities.Workspaces;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Enums;
using PanelForge.UnitTests.Common;
using Xunit;

namespace PanelForge.UnitTests.Application.Workspaces;

/// <summary>Các handler thay thế WorkspaceService cũ — giữ nguyên các kịch bản kiểm thử trước đây.</summary>
public class WorkspaceHandlerTests
{
    private readonly Mock<IPanelForgeDbContext> _dbContextMock = new();
    private readonly Mock<IWorkspaceAuthorizationService> _authorizationServiceMock = new();
    private readonly Mock<IEmailService> _emailServiceMock = new();

    private AddWorkspaceMemberCommandHandler AddMember() => new(
        _dbContextMock.Object,
        _authorizationServiceMock.Object,
        _emailServiceMock.Object,
        NullLogger<AddWorkspaceMemberCommandHandler>.Instance);

    private void AllowManage(Guid actorId, Guid workspaceId)
        => _authorizationServiceMock
            .Setup(a => a.IsOwnerOrProducerAsync(actorId, workspaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

    [Fact]
    public async Task GetMyWorkspaces_ShouldFilterIsDeletedAndCountPendingTasks()
    {
        var userId = Guid.NewGuid();
        var owner = User.Create("owner@test.com", "Owner");
        owner.GetType().GetProperty("Id")?.SetValue(owner, userId);

        var workspace = StudioWorkspace.Create("Test Studio", owner.Id);
        workspace.GetType().GetProperty("Owner")?.SetValue(workspace, owner);

        var deletedMember = WorkspaceMember.Create(workspace.Id, Guid.NewGuid(), WorkspaceRole.Artist);
        deletedMember.GetType().GetProperty("IsDeleted")?.SetValue(deletedMember, true);
        var activeMember = WorkspaceMember.Create(workspace.Id, userId, WorkspaceRole.Writer);
        typeof(StudioWorkspace).GetProperty("Members")?.SetValue(workspace, new List<WorkspaceMember> { deletedMember, activeMember });

        var series = Series.Create(workspace.Id, "Series 1", ReadingDirection.LeftToRight);
        series.GetType().GetProperty("WorkspaceId")?.SetValue(series, workspace.Id);

        var pending = Assignment.Create(series.Id, Guid.NewGuid(), WorkflowEntityType.Page, Guid.NewGuid(), Guid.NewGuid(), userId, WorkspaceRole.Artist, "Task 1");
        pending.GetType().GetProperty("Series")?.SetValue(pending, series);

        var approved = Assignment.Create(series.Id, Guid.NewGuid(), WorkflowEntityType.Page, Guid.NewGuid(), Guid.NewGuid(), userId, WorkspaceRole.Artist, "Task 2");
        approved.MarkApproved();
        approved.GetType().GetProperty("Series")?.SetValue(approved, series);

        var deleted = Assignment.Create(series.Id, Guid.NewGuid(), WorkflowEntityType.Page, Guid.NewGuid(), Guid.NewGuid(), userId, WorkspaceRole.Artist, "Task 3");
        deleted.GetType().GetProperty("IsDeleted")?.SetValue(deleted, true);
        deleted.GetType().GetProperty("Series")?.SetValue(deleted, series);

        _dbContextMock.Setup(db => db.StudioWorkspaces).Returns(DbSetMockHelper.CreateDbSetMock(new List<StudioWorkspace> { workspace }));
        _dbContextMock.Setup(db => db.Assignments).Returns(DbSetMockHelper.CreateDbSetMock(new List<Assignment> { pending, approved, deleted }));
        _dbContextMock.Setup(db => db.Series).Returns(DbSetMockHelper.CreateDbSetMock(new List<Series> { series }));
        _dbContextMock.Setup(db => db.Chapters).Returns(DbSetMockHelper.CreateDbSetMock(new List<Chapter>()));

        var result = await new GetMyWorkspacesQueryHandler(_dbContextMock.Object)
            .Handle(new GetMyWorkspacesQuery(userId), CancellationToken.None);

        var dto = result.Value.Should().ContainSingle().Subject;
        dto.PendingTasks.Should().Be(1);   // chỉ task chưa duyệt, chưa xóa
        dto.MemberCount.Should().Be(1);    // bỏ member đã xóa
        dto.IsOwner.Should().BeTrue();
        dto.CurrentUserRole.Should().Be(WorkspaceRole.Producer);
        dto.SeriesCount.Should().Be(1);
    }

    [Fact]
    public async Task GetMyWorkspaces_ShouldSummariseSeriesAndLatestActivity()
    {
        var userId = Guid.NewGuid();
        var owner = User.Create("owner2@test.com", "Owner");
        owner.GetType().GetProperty("Id")?.SetValue(owner, userId);
        var workspace = StudioWorkspace.Create("Moonlight", owner.Id);
        workspace.GetType().GetProperty("Owner")?.SetValue(workspace, owner);
        typeof(StudioWorkspace).GetProperty("Members")?.SetValue(workspace, new List<WorkspaceMember>());

        Series MakeSeries(string title, DateTime at, bool deleted = false)
        {
            var s = Series.Create(workspace.Id, title, ReadingDirection.RightToLeft);
            s.CreatedAt = at;
            s.IsDeleted = deleted;
            return s;
        }
        var oldest = MakeSeries("A", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var middle = MakeSeries("B", new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc));
        var newer = MakeSeries("C", new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc));
        var newest = MakeSeries("D", new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc));
        var removed = MakeSeries("X", new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc), deleted: true);

        var chapter = Chapter.Create(oldest.Id, 1, "Ch1");
        chapter.GetType().GetProperty("Series")?.SetValue(chapter, oldest);
        var chapterEdited = new DateTime(2026, 5, 10, 0, 0, 0, DateTimeKind.Utc);
        chapter.UpdatedAt = chapterEdited;

        _dbContextMock.Setup(db => db.StudioWorkspaces).Returns(DbSetMockHelper.CreateDbSetMock(new List<StudioWorkspace> { workspace }));
        _dbContextMock.Setup(db => db.Assignments).Returns(DbSetMockHelper.CreateDbSetMock(new List<Assignment>()));
        _dbContextMock.Setup(db => db.Series).Returns(DbSetMockHelper.CreateDbSetMock(new List<Series> { oldest, middle, newer, newest, removed }));
        _dbContextMock.Setup(db => db.Chapters).Returns(DbSetMockHelper.CreateDbSetMock(new List<Chapter> { chapter }));

        var result = await new GetMyWorkspacesQueryHandler(_dbContextMock.Object)
            .Handle(new GetMyWorkspacesQuery(userId), CancellationToken.None);

        var dto = result.Value.Should().ContainSingle().Subject;
        dto.SeriesCount.Should().Be(4); // series đã xoá không tính
        dto.SeriesPreviews!.Select(p => p.Title).Should().Equal("D", "C", "B"); // 3 series mới nhất
        dto.LastActivityAt.Should().Be(chapterEdited); // sửa chương mới hơn mọi series
    }

    [Fact]
    public async Task AddMember_WhenTargetUserIsAdmin_ShouldFail_BR07()
    {
        var actorId = Guid.NewGuid();
        var workspace = StudioWorkspace.Create("Test Studio", actorId);
        var adminUser = User.Create("admin@test.com", "Admin User", role: SystemRole.Admin);
        AllowManage(actorId, workspace.Id);

        _dbContextMock.Setup(db => db.StudioWorkspaces).Returns(DbSetMockHelper.CreateDbSetMock(new List<StudioWorkspace> { workspace }));
        _dbContextMock.Setup(db => db.Users).Returns(DbSetMockHelper.CreateDbSetMock(new List<User> { adminUser }));
        _dbContextMock.Setup(db => db.WorkspaceMembers).Returns(DbSetMockHelper.CreateDbSetMock(new List<WorkspaceMember>()));

        var result = await AddMember().Handle(
            new AddWorkspaceMemberCommand(actorId, workspace.Id, adminUser.Email, WorkspaceRole.Artist), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().BeNull(); // → 400
        result.ErrorMessage.Should().Contain("Administrator");
    }

    [Fact]
    public async Task AddMember_WhenActorNotProducer_ShouldReturnForbidden()
    {
        var result = await AddMember().Handle(
            new AddWorkspaceMemberCommand(Guid.NewGuid(), Guid.NewGuid(), "x@test.com", WorkspaceRole.Artist), CancellationToken.None);

        result.ErrorCode.Should().Be(ResultErrorCodes.Forbidden);
    }

    [Fact]
    public async Task AddMember_WhenEmailHasNoAccount_ShouldCreatePasswordlessInvitedUser_AndSendOtp()
    {
        var actorId = Guid.NewGuid();
        var workspace = StudioWorkspace.Create("Test Studio", actorId);
        AllowManage(actorId, workspace.Id);

        var users = new List<User>();
        _dbContextMock.Setup(db => db.StudioWorkspaces).Returns(DbSetMockHelper.CreateDbSetMock(new List<StudioWorkspace> { workspace }));
        _dbContextMock.Setup(db => db.Users).Returns(DbSetMockHelper.CreateDbSetMock(users));
        _dbContextMock.Setup(db => db.WorkspaceMembers).Returns(DbSetMockHelper.CreateDbSetMock(new List<WorkspaceMember>()));

        var result = await AddMember().Handle(
            new AddWorkspaceMemberCommand(actorId, workspace.Id, "New.Artist@Test.com", WorkspaceRole.Artist), CancellationToken.None);

        var invited = users.Should().ContainSingle().Subject;
        invited.Email.Should().Be("new.artist@test.com");
        invited.PasswordHash.Should().BeNull("tài khoản được mời không được có mật khẩu mặc định");
        invited.IsEmailConfirmed.Should().BeFalse();
        invited.PasswordResetToken.Should().MatchRegex("^[0-9]{6}$");
        invited.PasswordResetTokenExpiresAt.Should().BeAfter(DateTime.UtcNow);
        result.Value!.Role.Should().Be(WorkspaceRole.Artist);

        _dbContextMock.Verify(db => db.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _emailServiceMock.Verify(e => e.SendWorkspaceInvitationAsync(
            "new.artist@test.com", It.IsAny<string>(), "Test Studio", "Artist",
            invited.PasswordResetToken, It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AddMember_WhenEmailSendingFails_ShouldStillAddMember()
    {
        var actorId = Guid.NewGuid();
        var workspace = StudioWorkspace.Create("Test Studio", actorId);
        var existingUser = User.Create("writer@test.com", "Writer", "hash"); // đã có mật khẩu → không phải lời mời chờ kích hoạt
        AllowManage(actorId, workspace.Id);
        _emailServiceMock
            .Setup(e => e.SendWorkspaceInvitationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ArgumentNullException("SenderEmail"));

        _dbContextMock.Setup(db => db.StudioWorkspaces).Returns(DbSetMockHelper.CreateDbSetMock(new List<StudioWorkspace> { workspace }));
        _dbContextMock.Setup(db => db.Users).Returns(DbSetMockHelper.CreateDbSetMock(new List<User> { existingUser }));
        _dbContextMock.Setup(db => db.WorkspaceMembers).Returns(DbSetMockHelper.CreateDbSetMock(new List<WorkspaceMember>()));

        var result = await AddMember().Handle(
            new AddWorkspaceMemberCommand(actorId, workspace.Id, existingUser.Email, WorkspaceRole.Writer), CancellationToken.None);

        result.Value!.UserId.Should().Be(existingUser.Id);
        existingUser.PasswordResetToken.Should().BeNull("user đã có tài khoản không cần OTP kích hoạt");
        _dbContextMock.Verify(db => db.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateWorkspace_WithoutCanCreateStudio_ShouldReturnUnauthorized_BR22()
    {
        await using var db = InMemoryDb.Create();
        var user = User.Create("u@test.com", "U");
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var result = await new CreateWorkspaceCommandHandler(db)
            .Handle(new CreateWorkspaceCommand(user.Id, "Studio", 0), CancellationToken.None);

        result.ErrorCode.Should().Be(ResultErrorCodes.Unauthorized);
    }

    [Fact]
    public async Task CreateWorkspace_WithPermission_ShouldAddOwnerAsProducer()
    {
        await using var db = InMemoryDb.Create();
        var user = User.Create("p@test.com", "P");
        user.GrantStudioCreation();
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var result = await new CreateWorkspaceCommandHandler(db)
            .Handle(new CreateWorkspaceCommand(user.Id, "  My Studio ", 0), CancellationToken.None);

        result.Value!.IsOwner.Should().BeTrue();
        result.Value.CurrentUserRole.Should().Be(WorkspaceRole.Producer);
        db.WorkspaceMembers.Should().ContainSingle(m => m.UserId == user.Id && m.Role == WorkspaceRole.Producer);
    }

    [Fact]
    public async Task RemoveMember_WhenTargetIsOwner_ShouldFail()
    {
        await using var db = InMemoryDb.Create();
        var ownerId = Guid.NewGuid();
        var workspace = StudioWorkspace.Create("Studio", ownerId);
        db.StudioWorkspaces.Add(workspace);
        await db.SaveChangesAsync();

        var result = await new RemoveWorkspaceMemberCommandHandler(db, _authorizationServiceMock.Object)
            .Handle(new RemoveWorkspaceMemberCommand(ownerId, workspace.Id, ownerId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Chủ sở hữu");
    }
}
