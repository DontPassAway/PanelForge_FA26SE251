using FluentAssertions;
using Moq;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.SeriesMembers.Commands.AddSeriesMember;
using PanelForge.Application.Features.Workspaces.Commands.UpdateMemberRole;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Entities.Identity;
using PanelForge.Domain.Entities.Workspaces;
using PanelForge.Domain.Enums;
using PanelForge.Infrastructure.Services;
using PanelForge.UnitTests.Common;
using Xunit;

namespace PanelForge.UnitTests.Application.SeriesMembers;

/// <summary>Workspace role = nguồn role duy nhất; Series Membership = ranh giới truy cập Series.</summary>
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
        _db.Setup(d => d.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
    }

    private WorkspaceAuthorizationService Auth => new(_db.Object);

    private Mock<IWorkspaceAuthorizationService> ProducerActor()
    {
        var auth = new Mock<IWorkspaceAuthorizationService>();
        auth.Setup(a => a.IsOwnerOrProducerAsync(_ownerId, _workspace.Id, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        return auth;
    }

    [Fact]
    public async Task SeriesAccess_RequiresSeriesMembership_AndActiveWorkspaceMembership()
    {
        _wsMembers.Add(WorkspaceMember.Create(_workspace.Id, _artist.Id, WorkspaceRole.Artist));

        // Thành viên Workspace nhưng chưa vào Series → không truy cập được Series
        (await Auth.IsSeriesMemberAsync(_artist.Id, _series.Id)).Should().BeFalse();

        _seriesMembers.Add(SeriesMember.Create(_series.Id, _artist.Id, WorkspaceRole.Artist));
        (await Auth.IsSeriesMemberAsync(_artist.Id, _series.Id)).Should().BeTrue();
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
    public async Task HasSeriesRole_UsesWorkspaceRole_NotLegacySeriesRole()
    {
        _wsMembers.Add(WorkspaceMember.Create(_workspace.Id, _artist.Id, WorkspaceRole.Artist));
        _seriesMembers.Add(SeriesMember.Create(_series.Id, _artist.Id, WorkspaceRole.Writer)); // dòng cũ lệch role

        (await Auth.HasSeriesRoleAsync(_artist.Id, _series.Id, [WorkspaceRole.Artist])).Should().BeTrue();
        (await Auth.HasSeriesRoleAsync(_artist.Id, _series.Id, [WorkspaceRole.Writer])).Should().BeFalse();
    }

    [Fact]
    public async Task ManualAddToSeries_InheritsWorkspaceRole_IgnoringRequestedRole()
    {
        _wsMembers.Add(WorkspaceMember.Create(_workspace.Id, _artist.Id, WorkspaceRole.Artist));

        var result = await new AddSeriesMemberCommandHandler(_db.Object, ProducerActor().Object)
            .Handle(new AddSeriesMemberCommand(_ownerId, _series.Id, _artist.Id, WorkspaceRole.Editor), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Role.Should().Be(WorkspaceRole.Artist);
        _seriesMembers.Should().ContainSingle(m => m.UserId == _artist.Id && m.Role == WorkspaceRole.Artist);
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
    public async Task WorkspaceRoleChange_SyncsSeriesMembershipRoles()
    {
        var wsMember = WorkspaceMember.Create(_workspace.Id, _artist.Id, WorkspaceRole.Artist);
        typeof(WorkspaceMember).GetProperty(nameof(WorkspaceMember.User))!.SetValue(wsMember, _artist); // mock không chạy Include
        _wsMembers.Add(wsMember);
        _seriesMembers.Add(SeriesMember.Create(_series.Id, _artist.Id, WorkspaceRole.Artist));
        var otherSeriesMember = SeriesMember.Create(Guid.NewGuid(), _artist.Id, WorkspaceRole.Artist); // Series của Workspace khác
        _seriesMembers.Add(otherSeriesMember);

        var result = await new UpdateMemberRoleCommandHandler(_db.Object, ProducerActor().Object)
            .Handle(new UpdateMemberRoleCommand(_ownerId, _workspace.Id, _artist.Id, WorkspaceRole.Letterer), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _seriesMembers.Single(m => m.SeriesId == _series.Id).Role.Should().Be(WorkspaceRole.Letterer);
        otherSeriesMember.Role.Should().Be(WorkspaceRole.Artist);
        _db.Verify(d => d.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
