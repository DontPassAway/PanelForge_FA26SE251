using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PanelForge.Domain.Entities.Workspaces;
using PanelForge.Domain.Enums;
using PanelForge.Infrastructure.Persistence;
using Xunit;

namespace PanelForge.UnitTests.Infrastructure;

/// <summary>
/// BR-23 / MF-02.04: lời mời phải được lưu Pending. Pending = 0 là giá trị CLR mặc định của enum, nên nếu cột có
/// HasDefaultValue(Active) thì EF coi 0 là "chưa gán", không gửi xuống và DB ghi Active (lời mời tự được chấp nhận).
/// InMemory không tái hiện được default của DB, nên kiểm trên metadata model của Npgsql.
/// </summary>
public class WorkspaceMemberStatusMappingTests
{
    [Fact]
    public void Status_ShouldNotHaveDatabaseDefault_SoPendingIsAlwaysSent()
    {
        var options = new DbContextOptionsBuilder<PanelForgeDbContext>()
            .UseNpgsql("Host=localhost;Database=model_only") // chỉ dựng model, không mở kết nối
            .Options;
        using var db = new PanelForgeDbContext(options);

        var status = db.Model.FindEntityType(typeof(WorkspaceMember))!.FindProperty(nameof(WorkspaceMember.Status))!;

        // GetDefaultValue() trả giá trị CLR mặc định khi không cấu hình, nên kiểm thẳng annotation của provider quan hệ
        status.FindAnnotation(Microsoft.EntityFrameworkCore.Metadata.RelationalAnnotationNames.DefaultValue)
            .Should().BeNull("cột có default thì EF bỏ qua Pending (= 0) và DB ghi Active");
        status.GetDefaultValueSql().Should().BeNull();
    }

    [Fact]
    public void Create_WithPending_KeepsPending()
    {
        WorkspaceMember.Create(Guid.NewGuid(), Guid.NewGuid(), WorkspaceRole.Artist, WorkspaceMemberStatus.Pending)
            .Status.Should().Be(WorkspaceMemberStatus.Pending);
    }
}
