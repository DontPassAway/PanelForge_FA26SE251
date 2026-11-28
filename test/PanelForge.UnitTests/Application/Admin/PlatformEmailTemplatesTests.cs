using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Email;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.Admin.Commands.SendTestEmailTemplate;
using PanelForge.Application.Features.Admin.Commands.UpdateEmailTemplate;
using PanelForge.Application.Features.Admin.Queries.GetEmailTemplates;
using PanelForge.Application.Features.Admin.Queries.PreviewEmailTemplate;
using PanelForge.Domain.Entities.Identity;
using PanelForge.Domain.Enums;
using PanelForge.Infrastructure.Persistence;
using PanelForge.UnitTests.Common;
using Xunit;

namespace PanelForge.UnitTests.Application.Admin;

/// <summary>
/// Admin sửa nội dung email hệ thống bằng chữ thường (BR-18 audit, append-only): bố cục và ô mã OTP do hệ thống dựng,
/// chữ và giá trị đều được HTML-encode, biến không có trong loại email bị từ chối, lưu lại nội dung mặc định = về mặc định.
/// </summary>
public class PlatformEmailTemplatesTests
{
    private readonly PanelForgeDbContext _db = InMemoryDb.Create();

    private static readonly EmailTemplateContent Custom = new(
        "Mã đặt lại cho {{name}}", "Chào {{name}},", "Mã của bạn là:", "Còn hiệu lực {{validFor}}.", "PanelForge");

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
        result.Value.Should().OnlyContain(t => !t.IsCustomized && t.Content == t.DefaultContent);
    }

    [Fact]
    public async Task Update_SavesNewVersion_AndWritesAudit()
    {
        var adminId = AdminId();

        var result = await Update().Handle(
            new UpdateEmailTemplateCommand(adminId, EmailTemplateCatalog.PasswordReset, Custom), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.IsCustomized.Should().BeTrue();
        result.Value.Content.Intro.Should().Be("Mã của bạn là:");
        result.Value.ChangedByName.Should().Be("Quản Trị");
        (await _db.AuditLogs.AnyAsync(a => a.Action == "PLATFORM_EMAIL_TEMPLATE_CHANGED")).Should().BeTrue();
    }

    [Theory]
    [InlineData("", "Chào", "Mở đầu")]
    [InlineData("Tiêu đề", "", "Mở đầu")]
    [InlineData("Tiêu đề", "Chào", "")]
    [InlineData("Tiêu đề {{studio}}", "Chào", "Mở đầu")]
    public async Task Update_RejectsEmptyPartsOrPlaceholdersTheEmailDoesNotHave(string subject, string greeting, string intro)
    {
        var result = await Update().Handle(new UpdateEmailTemplateCommand(
            AdminId(), EmailTemplateCatalog.PasswordReset, new(subject, greeting, intro, "", "")), CancellationToken.None);

        result.ErrorCode.Should().Be(ResultErrorCodes.Validation);
        _db.PlatformEmailTemplates.Should().BeEmpty();
    }

    [Fact]
    public async Task Update_UnknownKey_IsNotFound()
    {
        var result = await Update().Handle(new UpdateEmailTemplateCommand(AdminId(), "Nope", Custom), CancellationToken.None);

        result.ErrorCode.Should().Be(ResultErrorCodes.NotFound);
    }

    [Fact]
    public async Task SavingDefaultContent_MarksTemplateAsDefaultAgain()
    {
        var adminId = AdminId();
        var definition = EmailTemplateCatalog.Find(EmailTemplateCatalog.ChangePasswordOtp)!;
        await Update().Handle(new UpdateEmailTemplateCommand(adminId, definition.Key, Custom), CancellationToken.None);

        var result = await Update().Handle(
            new UpdateEmailTemplateCommand(adminId, definition.Key, definition.Default), CancellationToken.None);

        result.Value!.IsCustomized.Should().BeFalse();
        _db.PlatformEmailTemplates.Should().HaveCount(2);
    }

    [Fact]
    public void Render_AlwaysShowsTheCode_AndEncodesTextAndValues()
    {
        var definition = EmailTemplateCatalog.Find(EmailTemplateCatalog.StudioInviteNewAccount)!;
        var content = new EmailTemplateContent(
            "Mời vào {{studio}}", "Chào {{name}}", "Dòng 1 <b>\nDòng 2", "", "");

        var (subject, body) = EmailTemplateCatalog.Render(definition, content, new Dictionary<string, string>
        {
            ["name"] = "<script>An</script>", ["studio"] = "A & B", ["code"] = "123456"
        });

        subject.Should().Be("Mời vào A & B");
        body.Should().Contain("&lt;script&gt;An&lt;/script&gt;")
            .And.Contain("Dòng 1 &lt;b&gt;<br/>Dòng 2")
            .And.Contain(">123456</span>")
            .And.NotContain("<script>");
    }

    [Fact]
    public void Render_EmailWithoutCode_HasNoCodeBox()
    {
        var definition = EmailTemplateCatalog.Find(EmailTemplateCatalog.StudioInviteExistingAccount)!;

        var (_, body) = EmailTemplateCatalog.Render(definition, definition.Default, EmailTemplateCatalog.SampleValues("An"));

        body.Should().NotContain("482913");
    }

    [Fact]
    public void Defaults_PassTheirOwnValidation()
    {
        foreach (var d in EmailTemplateCatalog.All)
            EmailTemplateCatalog.Validate(d, d.Default).Should().BeNull(d.Key);
    }

    [Fact]
    public async Task Preview_RendersDraftWithSampleData_WithoutSaving()
    {
        var result = await new PreviewEmailTemplateQueryHandler().Handle(
            new PreviewEmailTemplateQuery(EmailTemplateCatalog.PasswordReset, Custom), CancellationToken.None);

        result.Value!.Subject.Should().Be("Mã đặt lại cho Nguyễn Văn An");
        result.Value.BodyHtml.Should().Contain("482913").And.Contain("15 phút");
        _db.PlatformEmailTemplates.Should().BeEmpty();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SendTest_SendsDraftToAdmin_AndReportsDeliveryFailure(bool delivered)
    {
        var adminId = AdminId();
        var email = new Mock<IEmailService>();
        email.Setup(e => e.SendEmailAsync("admin@panelforge.vn", "[Thử] Mã đặt lại cho Quản Trị", It.IsAny<string>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(delivered);

        var result = await new SendTestEmailTemplateCommandHandler(_db, email.Object).Handle(
            new SendTestEmailTemplateCommand(adminId, EmailTemplateCatalog.PasswordReset, Custom), CancellationToken.None);

        result.IsSuccess.Should().Be(delivered);
        if (!delivered) result.ErrorCode.Should().Be(ResultErrorCodes.EmailDeliveryFailed);
        _db.PlatformEmailTemplates.Should().BeEmpty();
    }
}
