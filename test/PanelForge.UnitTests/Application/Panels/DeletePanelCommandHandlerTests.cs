using FluentAssertions;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Panels.Commands.DeletePanel;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Enums;
using PanelForge.Infrastructure.Persistence;
using PanelForge.UnitTests.Common;
using Xunit;

namespace PanelForge.UnitTests.Application.Panels;

/// <summary>Xoá Panel: không để lại Assignment trỏ tới Panel đã mất.</summary>
public class DeletePanelCommandHandlerTests
{
    private readonly PanelForgeDbContext _db = InMemoryDb.Create();
    private readonly Panel _panel = Panel.Create(Guid.NewGuid(), 1, 1, "{\"x\":0,\"y\":0,\"width\":10,\"height\":10}");

    public DeletePanelCommandHandlerTests()
    {
        _db.Panels.Add(_panel);
        _db.SaveChanges();
    }

    private Task<Result<bool>> Delete() =>
        new DeletePanelCommandHandler(_db).Handle(new DeletePanelCommand(_panel.Id, Guid.NewGuid()), default);

    [Fact]
    public async Task Handle_WhenPanelHasAssignment_ConflictsAndKeepsPanel()
    {
        _db.Assignments.Add(Assignment.Create(Guid.NewGuid(), Guid.NewGuid(), WorkflowEntityType.Panel, _panel.Id,
            Guid.NewGuid(), Guid.NewGuid(), WorkspaceRole.Artist, "Vẽ panel"));
        await _db.SaveChangesAsync();

        var result = await Delete();

        result.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        _db.Panels.Should().ContainSingle();
    }

    [Fact]
    public async Task Handle_WhenNoAssignment_SoftDeletesPanel()
    {
        var result = await Delete();

        result.IsSuccess.Should().BeTrue();
        _db.Panels.Should().BeEmpty();
    }
}
