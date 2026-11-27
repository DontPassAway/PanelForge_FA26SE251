using FluentAssertions;
using Moq;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.SeriesMembers.Commands.AddSeriesMember;
using PanelForge.Application.Features.Workspaces.Commands.UpdateMemberRole;
using PanelForge.Application.Features.Workspaces.Commands.RemoveMember;
using PanelForge.Application.Common;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Entities.Identity;
using PanelForge.Domain.Entities.Workspaces;
using PanelForge.Domain.Enums;
using PanelForge.Infrastructure.Services;
using PanelForge.UnitTests.Common;
using Xunit;

namespace PanelForge.UnitTests.Application.SeriesMembers;

/// <summary>Spec D10: Studio chỉ có Producer / Member; vai trò chuyên môn nằm ở Series Membership.</summary>
public class SeriesMembershipRulesTests
{
    private readonly Guid _ownerId = Guid.NewGuid();
    private readonly User _artist = User.Create("artist@test.com", "Artist");
    private readonly StudioWorkspace _workspace;
    private readonly Series _series;
    private readonly List<WorkspaceMember> _wsMembers = [];
    private readonly List<SeriesMember> _seriesMembers = [];
    private readonly Mock<IPanelForgeDbContext> _db = new();

    public SeriesMembershipRulesTests()
    {
        _workspace = StudioWorkspace.Create("Studio", _ownerId);
        _series = Series.Create(_workspace.Id, "S", ReadingDirection.RightToLeft);
        _db.Setup(d => d.Users).Returns(DbSetMockHelper.CreateDbSetMock(new List<User> { _artist }));
        _db.Setup(d => d.StudioWorkspaces).Returns(DbSetMockHelper.CreateDbSetMock(new List<StudioWorkspace> { _workspace }));
        _db.Setup(d => d.Series).Returns(DbSetMockHelper.CreateDbSetMock(new List<Series> { _series }));
        _db.Setup(d => d.WorkspaceMembers).Returns(DbSetMockHelper.CreateDbSetMock(_wsMembers));
        _db.Setup(d => d.SeriesMembers).Returns(DbSetMockHelper.CreateDbSetMock(_seriesMembers));
        _db.Setup(d => d.Assignments).Returns(DbSetMockHelper.CreateDbSetMock(_assignments));
        _db.Setup(d => d.Submissions).Returns(DbSetMockHelper.CreateDbSetMock(new List<Submission>()));
        _db.Setup(d => d.StageTasks).Returns(DbSetMockHelper.CreateDbSetMock(new List<StageTask>()));
        _db.Setup(d => d.StageTaskLots).Returns(DbSetMockHelper.CreateDbSetMock(new List<StageTaskLot>()));
        _db.Setup(d => d.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
    }

    private readonly List<Assignment> _assignments = [];

    private Assignment OpenAssignmentFor(Guid userId) =>
        Assignment.Create(_series.Id, Guid.NewGuid(), WorkflowEntityType.Page, Guid.NewGuid(), Guid.NewGuid(),
            userId, WorkspaceRole.Artist, "Pencil trang 1");

    [Fact]
    public async Task ProducerDemotion_WithOpenWork_IsBlocked()
    {
        _wsMembers.Add(WorkspaceMember.Create(_workspace.Id, _artist.Id, WorkspaceRole.Producer));
        _assignments.Add(OpenAssignmentFor(_artist.Id));

        var result = await new UpdateMemberRoleCommandHandler(_db.Object, ProducerActor().Object)
            .Handle(new UpdateMemberRoleCommand(_ownerId, _workspace.Id, _artist.Id, WorkspaceRole.Member), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        _wsMembers.Single().Role.Should().Be(WorkspaceRole.Producer);
        _db.Verify(d => d.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RemoveMember_CancelsUnsubmittedLegacyAssignments()
    {
        _wsMembers.Add(WorkspaceMember.Create(_workspace.Id, _artist.Id, WorkspaceRole.Artist));
        var open = OpenAssignmentFor(_artist.Id);
        _assignments.Add(open);

        var result = await new RemoveWorkspaceMemberCommandHandler(_db.Object, ProducerActor().Object)
            .Handle(new RemoveWorkspaceMemberCommand(_ownerId, _workspace.Id, _artist.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        open.CancelReason.Should().Be("Người nhận đã rời Studio.");
        _assignments.Should().NotContain(open); // Remove → xoá mềm khi lưu
    }

    private WorkspaceAuthorizationService Auth => new(_db.Object);

    private Mock<IWorkspaceAuthorizationService> ProducerActor()
    {
        var auth = new Mock<IWorkspaceAuthorizationService>();
        auth.Setup(a => a.IsOwnerOrProducerAsync(_ownerId, _workspace.Id, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        return auth;
    }

    [Fact]
    public async Task SeriesAccess_ActiveStudioMemberCanRead_ButHasNoRoleUntilAddedToSeries()
    {
        _wsMembers.Add(WorkspaceMember.Create(_workspace.Id, _artist.Id, WorkspaceRole.Member));

        (await Auth.IsSeriesMemberAsync(_artist.Id, _series.Id)).Should().BeTrue();
        (await Auth.HasSeriesRoleAsync(_artist.Id, _series.Id, [WorkspaceRole.Artist])).Should().BeFalse();
    }

    [Theory]
    [InlineData(WorkspaceMemberStatus.Pending)]
    [InlineData(WorkspaceMemberStatus.Rejected)]
    public async Task StaleSeriesMembership_WithoutActiveWorkspaceMembership_IsDenied(WorkspaceMemberStatus status)
    {
        _wsMembers.Add(WorkspaceMember.Create(_workspace.Id, _artist.Id, WorkspaceRole.Artist, status));
        _seriesMembers.Add(SeriesMember.Create(_series.Id, _artist.Id, WorkspaceRole.Artist));

        (await Auth.IsSeriesMemberAsync(_artist.Id, _series.Id)).Should().BeFalse();
        (await Auth.HasSeriesRoleAsync(_artist.Id, _series.Id, [WorkspaceRole.Artist])).Should().BeFalse();
    }

    [Fact]
    public async Task RemovedFromWorkspace_LosesSeriesAccess()
    {
        _seriesMembers.Add(SeriesMember.Create(_series.Id, _artist.Id, WorkspaceRole.Artist)); // không còn dòng Workspace

        (await Auth.IsSeriesMemberAsync(_artist.Id, _series.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task HasWorkspaceRole_AcceptsSpecialistRoleHeldInAnySeriesOfStudio()
    {
        _wsMembers.Add(WorkspaceMember.Create(_workspace.Id, _artist.Id, WorkspaceRole.Member));
        _seriesMembers.Add(SeriesMember.Create(_series.Id, _artist.Id, WorkspaceRole.Editor));

        (await Auth.HasWorkspaceRoleAsync(_artist.Id, _workspace.Id, [WorkspaceRole.Editor])).Should().BeTrue();
        (await Auth.HasWorkspaceRoleAsync(_artist.Id, _workspace.Id, [WorkspaceRole.Artist])).Should().BeFalse();

        // Người khác trong Studio không được "mượn" vai trò Editor của _artist
        var other = Guid.NewGuid();
        _wsMembers.Add(WorkspaceMember.Create(_workspace.Id, other, WorkspaceRole.Member));
        (await Auth.HasWorkspaceRoleAsync(other, _workspace.Id, [WorkspaceRole.Editor])).Should().BeFalse();
    }

    [Fact]
    public async Task HasSeriesRole_UsesSeriesRole()
    {
        _wsMembers.Add(WorkspaceMember.Create(_workspace.Id, _artist.Id, WorkspaceRole.Member));
        _seriesMembers.Add(SeriesMember.Create(_series.Id, _artist.Id, WorkspaceRole.Writer));

        (await Auth.HasSeriesRoleAsync(_artist.Id, _series.Id, [WorkspaceRole.Writer])).Should().BeTrue();
        (await Auth.HasSeriesRoleAsync(_artist.Id, _series.Id, [WorkspaceRole.Artist])).Should().BeFalse();
    }

    [Fact]
    public async Task ManualAddToSeries_UsesRequestedRole()
    {
        _wsMembers.Add(WorkspaceMember.Create(_workspace.Id, _artist.Id, WorkspaceRole.Member));

        var result = await new AddSeriesMemberCommandHandler(_db.Object, ProducerActor().Object)
            .Handle(new AddSeriesMemberCommand(_ownerId, _series.Id, _artist.Id, WorkspaceRole.Editor), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Role.Should().Be(WorkspaceRole.Editor);
        _seriesMembers.Should().ContainSingle(m => m.UserId == _artist.Id && m.Role == WorkspaceRole.Editor);
    }

    [Fact]
    public async Task ManualAddToSeries_WithMemberRole_IsRejected()
    {
        _wsMembers.Add(WorkspaceMember.Create(_workspace.Id, _artist.Id, WorkspaceRole.Member));

        var result = await new AddSeriesMemberCommandHandler(_db.Object, ProducerActor().Object)
            .Handle(new AddSeriesMemberCommand(_ownerId, _series.Id, _artist.Id, WorkspaceRole.Member), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        _seriesMembers.Should().BeEmpty();
    }

    [Fact]
    public async Task ManualAddToSeries_PendingWorkspaceMember_IsRejected()
    {
        _wsMembers.Add(WorkspaceMember.Create(_workspace.Id, _artist.Id, WorkspaceRole.Artist, WorkspaceMemberStatus.Pending));

        var result = await new AddSeriesMemberCommandHandler(_db.Object, ProducerActor().Object)
            .Handle(new AddSeriesMemberCommand(_ownerId, _series.Id, _artist.Id, WorkspaceRole.Artist), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        _seriesMembers.Should().BeEmpty();
    }

    [Fact]
    public async Task StudioRole_SpecialistRole_IsRejected()
    {
        _wsMembers.Add(WorkspaceMember.Create(_workspace.Id, _artist.Id, WorkspaceRole.Member));

        var result = await new UpdateMemberRoleCommandHandler(_db.Object, ProducerActor().Object)
            .Handle(new UpdateMemberRoleCommand(_ownerId, _workspace.Id, _artist.Id, WorkspaceRole.Artist), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ResultErrorCodes.Validation);
    }

    [Fact]
    public async Task StudioRoleChange_DoesNotTouchSeriesRoles()
    {
        var wsMember = WorkspaceMember.Create(_workspace.Id, _artist.Id, WorkspaceRole.Member);
        typeof(WorkspaceMember).GetProperty(nameof(WorkspaceMember.User))!.SetValue(wsMember, _artist); // mock không chạy Include
        _wsMembers.Add(wsMember);
        _seriesMembers.Add(SeriesMember.Create(_series.Id, _artist.Id, WorkspaceRole.Artist));
        var otherSeriesMember = SeriesMember.Create(Guid.NewGuid(), _artist.Id, WorkspaceRole.Artist); // Series của Workspace khác
        _seriesMembers.Add(otherSeriesMember);

        var result = await new UpdateMemberRoleCommandHandler(_db.Object, ProducerActor().Object)
            .Handle(new UpdateMemberRoleCommand(_ownerId, _workspace.Id, _artist.Id, WorkspaceRole.Producer), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _seriesMembers.Single(m => m.SeriesId == _series.Id).Role.Should().Be(WorkspaceRole.Artist);
        otherSeriesMember.Role.Should().Be(WorkspaceRole.Artist);
        _db.Verify(d => d.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
