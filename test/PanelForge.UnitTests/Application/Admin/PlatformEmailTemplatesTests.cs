using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Email;
using PanelForge.Application.Features.Admin.Commands.UpdateEmailTemplate;
using PanelForge.Application.Features.Admin.Queries.GetEmailTemplates;
using PanelForge.Domain.Entities.Identity;
using PanelForge.Domain.Enums;
using PanelForge.Infrastructure.Persistence;
using PanelForge.UnitTests.Common;
using Xunit;

namespace PanelForge.UnitTests.Application.Admin;

/// <summary>
/// Admin sửa mẫu email hệ thống (BR-18 audit, append-only): biến lạ hoặc thiếu {{code}} bị từ chối,
/// giá trị thay vào nội dung được HTML-encode, lưu lại mẫu mặc định = về mặc định.
/// </summary>
public class PlatformEmailTemplatesTests
{
    private readonly PanelForgeDbContext _db = InMemoryDb.Create();

    private Guid AdminId()
    {
        var admin = User.Create("admin@panelforge.vn", "Quản Trị", role: SystemRole.Admin);
        _db.Users.Add(admin);
        _db.SaveChanges();
        return admin.Id;
    }

    private UpdateEmailTemplateCommandHandler Update() => new(_db);

    [Fact]
    public async Task Get_WhenNothingSaved_ReturnsDefaultsForEveryEmailType()
    {
        var result = await new GetEmailTemplatesQueryHandler(_db).Handle(new GetEmailTemplatesQuery(), CancellationToken.None);

        result.Value!.Select(t => t.Key).Should().Equal(EmailTemplateCatalog.All.Select(d => d.Key));
        result.Value.Should().OnlyContain(t => !t.IsCustomized && t.Subject == t.DefaultSubject);
    }

    [Fact]
    public async Task Update_SavesNewVersion_AndWritesAudit()
    {
        var adminId = AdminId();

        var result = await Update().Handle(new UpdateEmailTemplateCommand(
            adminId, EmailTemplateCatalog.EmailVerification, "Mã của bạn: {{code}}", "<p>Chào {{name}}, mã {{code}}</p>"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.IsCustomized.Should().BeTrue();
        result.Value.ChangedByName.Should().Be("Quản Trị");
        (await _db.AuditLogs.AnyAsync(a => a.Action == "PLATFORM_EMAIL_TEMPLATE_CHANGED")).Should().BeTrue();
    }

    [Theory]
    [InlineData("Tiêu đề", "<p>Thiếu mã</p>")]
    [InlineData("Tiêu đề {{password}}", "<p>{{code}}</p>")]
    [InlineData("", "<p>{{code}}</p>")]
    public async Task Update_RejectsMissingCodeUnknownPlaceholderOrEmptySubject(string subject, string body)
    {
        var adminId = AdminId();

        var result = await Update().Handle(
            new UpdateEmailTemplateCommand(adminId, EmailTemplateCatalog.PasswordReset, subject, body), CancellationToken.None);

        result.ErrorCode.Should().Be(ResultErrorCodes.Validation);
        _db.PlatformEmailTemplates.Should().BeEmpty();
    }

    [Fact]
    public async Task Update_UnknownKey_IsNotFound()
    {
        var result = await Update().Handle(
            new UpdateEmailTemplateCommand(AdminId(), "Nope", "x", "y"), CancellationToken.None);

        result.ErrorCode.Should().Be(ResultErrorCodes.NotFound);
    }

    [Fact]
    public async Task SavingDefaultContent_MarksTemplateAsDefaultAgain()
    {
        var adminId = AdminId();
        var definition = EmailTemplateCatalog.Find(EmailTemplateCatalog.ChangePasswordOtp)!;
        await Update().Handle(new UpdateEmailTemplateCommand(adminId, definition.Key, "Mã {{code}}", "<p>{{code}}</p>"), CancellationToken.None);

        var result = await Update().Handle(
            new UpdateEmailTemplateCommand(adminId, definition.Key, definition.DefaultSubject, definition.DefaultBodyHtml),
            CancellationToken.None);

        result.Value!.IsCustomized.Should().BeFalse();
        _db.PlatformEmailTemplates.Should().HaveCount(2);
    }

    [Fact]
    public void Render_EncodesValuesInBody_AndLeavesUnknownPlaceholders()
    {
        var (subject, body) = EmailTemplateCatalog.Render(
            "Mời vào {{studio}}",
            "<p>{{name}} · {{studio}} · {{other}}</p>",
            new Dictionary<string, string> { ["name"] = "<b>An</b>", ["studio"] = "A & B" });

        subject.Should().Be("Mời vào A & B");
        body.Should().Be("<p>&lt;b&gt;An&lt;/b&gt; · A &amp; B · {{other}}</p>");
    }

    [Fact]
    public void Defaults_PassTheirOwnValidation()
    {
        foreach (var d in EmailTemplateCatalog.All)
            EmailTemplateCatalog.Validate(d, d.DefaultSubject, d.DefaultBodyHtml).Should().BeNull(d.Key);
    }
}
