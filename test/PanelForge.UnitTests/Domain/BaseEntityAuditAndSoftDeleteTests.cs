using FluentAssertions;
using PanelForge.Domain.Common;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;
using Xunit;

namespace PanelForge.UnitTests.Domain;

public class BaseEntityAuditAndSoftDeleteTests
{
    private class TestEntity : BaseEntity { }

    [Fact]
    public void BaseEntity_ShouldInitializeWithDefaultValues()
    {
        // Act
        var entity = new TestEntity();

        // Assert
        entity.Id.Should().NotBeEmpty();
        entity.IsDeleted.Should().BeFalse();
        entity.DeletedAt.Should().BeNull();
        entity.DeletedBy.Should().BeNull();
        entity.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        entity.UpdatedAt.Should().BeNull();
        entity.CreatedBy.Should().BeNull();
        entity.UpdatedBy.Should().BeNull();
    }

    [Fact]
    public void ContentEntities_ShouldInheritAuditAndSoftDeleteProperties()
    {
        // Arrange
        var series = Series.Create(Guid.NewGuid(), "One Piece", ReadingDirection.RightToLeft);
        var chapter = Chapter.Create(series.Id, 1, "Romance Dawn");
        var scene = Scene.Create(chapter.Id, 1, "Luffy at sea");
        var panel = Panel.Create(Guid.NewGuid(), 1, 1, "{}");
        var element = Element.Create(panel.Id, ElementType.DialogueBalloon, 1, "Hello!");

        // Assert
        series.Should().BeAssignableTo<IAuditableEntity>();
        series.Should().BeAssignableTo<ISoftDelete>();
        series.IsDeleted.Should().BeFalse();

        chapter.Should().BeAssignableTo<IAuditableEntity>();
        chapter.Should().BeAssignableTo<ISoftDelete>();
        chapter.IsDeleted.Should().BeFalse();

        scene.Should().BeAssignableTo<IAuditableEntity>();
        scene.Should().BeAssignableTo<ISoftDelete>();

        panel.Should().BeAssignableTo<IAuditableEntity>();
        panel.Should().BeAssignableTo<ISoftDelete>();

        element.Should().BeAssignableTo<IAuditableEntity>();
        element.Should().BeAssignableTo<ISoftDelete>();
    }

    [Fact]
    public void Page_AsAggregateRoot_ShouldInheritAuditAndSoftDeleteProperties()
    {
        // Arrange
        var page = Page.CreateNew(Guid.NewGuid(), 1, LayoutFormat.StandardPage, 1200, 1800, 300);

        // Assert
        page.Should().BeAssignableTo<IAuditableEntity>();
        page.Should().BeAssignableTo<ISoftDelete>();
        page.IsDeleted.Should().BeFalse();
        page.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }
}
