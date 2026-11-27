using FluentAssertions;
using Moq;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.TaskGrid.Common;
using PanelForge.Application.Features.Workspaces.Commands.UpdateAutomationSettings;
using PanelForge.Application.Features.Workspaces.Models;
using PanelForge.Application.Features.Workspaces.Queries.GetAutomationSettings;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Entities.Workspaces;
using PanelForge.Domain.Enums;
using PanelForge.UnitTests.Common;
using Xunit;

namespace PanelForge.UnitTests.Application.Workspaces;

/// <summary>Assign Automation – Bước 1: cấu hình Studio (GET/PUT) và dọn gợi ý người duyệt khi Editor rời vai trò (N5).</summary>
public class AutomationSettingsTests
{
    private readonly Guid _workspaceId = Guid.NewGuid();
    private readonly Guid _producerId = Guid.NewGuid();
    private readonly List<WorkspaceAutomationSettings> _settings = [];
    private readonly Mock<IPanelForgeDbContext> _db = new();
    private readonly Mock<IWorkspaceAuthorizationService> _auth = new();

    public AutomationSettingsTests()
    {
        _db.Setup(d => d.WorkspaceAutomationSettings).Returns(DbSetMockHelper.CreateDbSetMock(_settings));
        _db.Setup(d => d.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _auth.Setup(a => a.IsOwnerOrProducerAsync(_producerId, _workspaceId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _auth.Setup(a => a.HasWorkspaceRoleAsync(_producerId, _workspaceId, It.IsAny<WorkspaceRole[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
    }

    private Task<Result<AutomationSettingsDto>> Put(Guid userId, UpdateAutomationSettingsRequest request) =>
        new UpdateAutomationSettingsCommandHandler(_db.Object, _auth.Object)
            .Handle(new UpdateAutomationSettingsCommand(userId, _workspaceId, request), default);

    private static UpdateAutomationSettingsRequest Request(int lead = 24, int? escalation = null, uint? rowVersion = null) =>
        new(true, AutoAssignMode.BalanceOnly, true, lead, escalation, rowVersion);

    [Fact]
    public async Task Get_WithoutRow_ReturnsDefaults_AllRulesOff()
    {
        var result = await new GetAutomationSettingsQueryHandler(_db.Object, _auth.Object)
            .Handle(new GetAutomationSettingsQuery(_producerId, _workspaceId), default);

        result.IsSuccess.Should().BeTrue();
        result.Value!.AutoNextStage.Should().BeFalse();
        result.Value.AutoRouteReview.Should().BeFalse();
        result.Value.AssignMode.Should().Be(AutoAssignMode.KeepPageOwner);
        result.Value.LeadHours.Should().Be(24);
        result.Value.RowVersion.Should().BeNull();
    }

    [Fact]
    public async Task Put_FirstTime_CreatesRow_ThenUpdatesSameRow()
    {
        (await Put(_producerId, Request(lead: 12, escalation: 48))).IsSuccess.Should().BeTrue();
        _settings.Should().ContainSingle();
        _settings[0].LeadHours.Should().Be(12);
        _settings[0].EscalationHours.Should().Be(48);
        _settings[0].AssignMode.Should().Be(AutoAssignMode.BalanceOnly);

        // xmin của bản ghi mock là 0
        (await Put(_producerId, Request(lead: 6, rowVersion: 0))).IsSuccess.Should().BeTrue();
        _settings.Should().ContainSingle();
        _settings[0].LeadHours.Should().Be(6);
    }

    [Fact]
    public async Task Put_StaleRowVersion_IsConflict()
    {
        (await Put(_producerId, Request())).IsSuccess.Should().BeTrue();

        var result = await Put(_producerId, Request(rowVersion: null));
        result.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(169, null)]
    [InlineData(24, 0)]
    [InlineData(24, 169)]
    public async Task Put_HoursOutOfRange_IsValidationError(int lead, int? escalation)
    {
        var result = await Put(_producerId, Request(lead, escalation));
        result.ErrorCode.Should().Be(ResultErrorCodes.Validation);
        _settings.Should().BeEmpty();
    }

    [Fact]
    public async Task Put_ByNonProducer_IsForbidden()
    {
        var result = await Put(Guid.NewGuid(), Request());
        result.ErrorCode.Should().Be(ResultErrorCodes.Forbidden);
        _settings.Should().BeEmpty();
    }

    [Fact]
    public async Task ReleaseReviewer_ReturnsUnderReviewToQueue_AndLeavesOtherReviewersAlone()
    {
        var editor = Guid.NewGuid();
        var otherEditor = Guid.NewGuid();
        var seriesId = Guid.NewGuid();
        StageTask Reviewing(Guid reviewer)
        {
            var t = StageTask.Create(seriesId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
            t.AssignTo(Guid.NewGuid(), true);
            t.Start(true);
            t.Submit(null, true);
            t.OpenReview(reviewer);
            return t;
        }
        var mine = Reviewing(editor);
        var theirs = Reviewing(otherEditor);
        _db.Setup(d => d.StageTasks).Returns(DbSetMockHelper.CreateDbSetMock(new List<StageTask> { mine, theirs }));

        await TaskGridRules.ReleaseReviewerAsync(_db.Object, editor, [seriesId], default);

        mine.Status.Should().Be(StageTaskStatus.Submitted);
        mine.ReviewerUserId.Should().BeNull();
        theirs.Status.Should().Be(StageTaskStatus.UnderReview);
        theirs.ReviewerUserId.Should().Be(otherEditor);
    }
}