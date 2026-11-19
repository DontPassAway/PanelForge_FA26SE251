using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using PanelForge.API.Common.Attributes;
using PanelForge.API.Controllers;
using PanelForge.Domain.Enums;
using Xunit;

namespace PanelForge.UnitTests.API;

public class BibleControllerPermissionTests
{
    private static MethodInfo GetAction(string methodName)
    {
        var method = typeof(BibleController).GetMethod(methodName);
        method.Should().NotBeNull($"Action {methodName} phải tồn tại trên BibleController");
        return method!;
    }

    private static WorkspaceRole[] GetAllowedRoles(MethodInfo method)
    {
        var attr = method.GetCustomAttribute<RequireWorkspaceRoleAttribute>();
        attr.Should().NotBeNull($"Action {method.Name} phải được bảo vệ bởi [RequireWorkspaceRole]");
        if (attr!.Arguments is { Length: > 0 } && attr.Arguments[0] is WorkspaceRole[] roles)
            return roles;
        return Array.Empty<WorkspaceRole>();
    }

    [Fact]
    public void ConfirmBibleEntryRevision_AllowsOnlyEditor_AndRejectsWriterProducerArtist()
    {
        var method = GetAction(nameof(BibleController.ConfirmBibleEntryRevision));
        var roles = GetAllowedRoles(method);

        roles.Should().ContainSingle()
            .Which.Should().Be(WorkspaceRole.Editor, "Q20: Chỉ Editor mới có quyền xác nhận concept và khoá Canon");
        roles.Should().NotContain(WorkspaceRole.Writer);
        roles.Should().NotContain(WorkspaceRole.Producer);
        roles.Should().NotContain(WorkspaceRole.Artist);
    }

    [Fact]
    public void SupersedeBibleEntryRevision_AllowsOnlyWriter_AndRejectsProducer()
    {
        var method = GetAction(nameof(BibleController.SupersedeBibleEntryRevision));
        var roles = GetAllowedRoles(method);

        roles.Should().ContainSingle()
            .Which.Should().Be(WorkspaceRole.Writer, "Q19/Q20: Producer chỉ đọc Bible; chỉ Writer mới được supersede bản đã khoá");
        roles.Should().NotContain(WorkspaceRole.Producer);
    }

    [Fact]
    public void CreateBibleEntry_AllowsOnlyWriter_AndRejectsProducer()
    {
        var method = GetAction(nameof(BibleController.CreateBibleEntry));
        var roles = GetAllowedRoles(method);

        roles.Should().ContainSingle()
            .Which.Should().Be(WorkspaceRole.Writer, "Q19: Chỉ Writer tạo mục Bible");
        roles.Should().NotContain(WorkspaceRole.Producer);
    }

    [Fact]
    public void AddBibleEntryRevision_AllowsOnlyWriter_AndRejectsProducer()
    {
        var method = GetAction(nameof(BibleController.AddBibleEntryRevision));
        var roles = GetAllowedRoles(method);

        roles.Should().ContainSingle()
            .Which.Should().Be(WorkspaceRole.Writer, "Q19: Chỉ Writer tạo revision nội dung chữ");
        roles.Should().NotContain(WorkspaceRole.Producer);
    }

    [Fact]
    public void RequestReworkBibleEntryRevision_AllowsWriterEditorProducer()
    {
        var method = GetAction(nameof(BibleController.RequestReworkBibleEntryRevision));
        var roles = GetAllowedRoles(method);

        roles.Should().BeEquivalentTo(new[] { WorkspaceRole.Producer, WorkspaceRole.Writer, WorkspaceRole.Editor },
            "Q22: Producer, Writer và Editor đều được yêu cầu Artist vẽ lại concept");
    }

    [Fact]
    public void CreateConceptComment_AllowsWriterAndEditor_RejectsProducerAndArtist()
    {
        var method = GetAction(nameof(BibleController.CreateConceptComment));
        var roles = GetAllowedRoles(method);

        roles.Should().BeEquivalentTo(new[] { WorkspaceRole.Writer, WorkspaceRole.Editor },
            "Góp ý gốc trên ConceptArt chỉ dành cho Writer và Editor");
        roles.Should().NotContain(WorkspaceRole.Producer);
        roles.Should().NotContain(WorkspaceRole.Artist);
    }

    [Fact]
    public void ReplyConceptComment_AllowsWriterEditorArtist_RejectsProducer()
    {
        var method = GetAction(nameof(BibleController.ReplyConceptComment));
        var roles = GetAllowedRoles(method);

        roles.Should().BeEquivalentTo(new[] { WorkspaceRole.Writer, WorkspaceRole.Editor, WorkspaceRole.Artist },
            "Trả lời góp ý cho phép Writer, Editor và Artist; Producer chỉ đọc");
        roles.Should().NotContain(WorkspaceRole.Producer);
    }

    [Fact]
    public void ResolveConceptComment_AllowsWriterAndEditor_RejectsProducerAndArtist()
    {
        var method = GetAction(nameof(BibleController.ResolveConceptComment));
        var roles = GetAllowedRoles(method);

        roles.Should().BeEquivalentTo(new[] { WorkspaceRole.Writer, WorkspaceRole.Editor },
            "Đánh dấu xử lý góp ý chỉ dành cho Writer và Editor");
        roles.Should().NotContain(WorkspaceRole.Producer);
        roles.Should().NotContain(WorkspaceRole.Artist);
    }

    [Fact]
    public void SetBibleEntryCrowd_AllowsWriterProducerEditor_AndRejectsArtist()
    {
        var method = GetAction(nameof(BibleController.SetBibleEntryCrowd));
        var roles = GetAllowedRoles(method);

        roles.Should().BeEquivalentTo([WorkspaceRole.Writer, WorkspaceRole.Producer, WorkspaceRole.Editor]);
        roles.Should().NotContain(WorkspaceRole.Artist);
    }

    [Fact]
    public void DefineBibleEntry_AllowsOnlyWriter_AndRejectsProducer()
    {
        var method = GetAction(nameof(BibleController.DefineBibleEntry));
        var roles = GetAllowedRoles(method);

        roles.Should().ContainSingle()
            .Which.Should().Be(WorkspaceRole.Writer, "BIB-13: Chỉ Writer mới có quyền viết định nghĩa mục nháp");
        roles.Should().NotContain(WorkspaceRole.Producer);
        roles.Should().NotContain(WorkspaceRole.Editor);
        roles.Should().NotContain(WorkspaceRole.Artist);
    }

    [Fact]
    public void SetChapterCastRevision_AllowsOnlyWriter_AndRejectsProducer()
    {
        var method = GetAction(nameof(BibleController.SetChapterCastRevision));
        var roles = GetAllowedRoles(method);

        roles.Should().ContainSingle()
            .Which.Should().Be(WorkspaceRole.Writer, "BIB-13: Chỉ Writer mới có quyền chọn bản hoặc trả về bản kế thừa");
        roles.Should().NotContain(WorkspaceRole.Producer);
        roles.Should().NotContain(WorkspaceRole.Editor);
        roles.Should().NotContain(WorkspaceRole.Artist);
    }
}
