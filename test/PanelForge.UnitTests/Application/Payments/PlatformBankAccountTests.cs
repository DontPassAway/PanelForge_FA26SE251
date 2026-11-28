using PanelForge.Application.Features.Payments.Models;
using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using PanelForge.API.Controllers;
using Microsoft.EntityFrameworkCore;
using Moq;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Authentication;
using PanelForge.Application.Features.Payments.Commands.UpdatePlatformBankAccount;
using PanelForge.Application.Features.Payments.Queries.GetPlatformBankAccount;
using PanelForge.Domain.Entities.Identity;
using PanelForge.Domain.Enums;
using PanelForge.Infrastructure.Persistence;
using PanelForge.UnitTests.Common;
using Xunit;

namespace PanelForge.UnitTests.Application.Payments;

/// <summary>
/// Q13 / BR-32 (đổi 2026-11-26, chốt lại 2026-11-28): tiền VietQR vào tài khoản ngân hàng của nền tảng,
/// Publisher cấu hình (bắt buộc 2FA, append-only, có audit) và duyệt / từ chối giao dịch. Admin chỉ đọc.
/// </summary>
public class PlatformBankAccountTests
{
    private readonly PanelForgeDbContext _db = InMemoryDb.Create();

    [Fact]
    public void WorkspacePaymentsController_EnforcesPublisherOnlyWrites_AndAdminDashboardRead()
    {
        // Class-level [Authorize] without Roles so BR-32 read endpoint can allow Admin while writes are Publisher-only
        var controllerAttr = typeof(WorkspacePaymentsController).GetCustomAttribute<AuthorizeAttribute>();
        controllerAttr.Should().NotBeNull();
        controllerAttr!.Roles.Should().BeNull();

        // BR-32 read-only payments list: Publisher and Admin
        var getPaymentsAttr = typeof(WorkspacePaymentsController)
            .GetMethod(nameof(WorkspacePaymentsController.GetWorkspacePayments))!
            .GetCustomAttribute<AuthorizeAttribute>();
        getPaymentsAttr.Should().NotBeNull();
        getPaymentsAttr!.Roles.Should().Be("Publisher,Admin");

        // Q13 / BR-32: VietQR approve / reject are strictly Publisher-only (Admin -> 403)
        foreach (var methodName in new[]
        {
            nameof(WorkspacePaymentsController.ApprovePayment),
            nameof(WorkspacePaymentsController.RejectPayment),
        })
        {
            var attr = typeof(WorkspacePaymentsController).GetMethod(methodName)!.GetCustomAttribute<AuthorizeAttribute>();
            attr.Should().NotBeNull($"{methodName} must declare [Authorize(Roles = \"Publisher\")]");
            attr!.Roles.Should().Be("Publisher");
        }
    }

    [Fact]
    public void PlatformPaymentsController_PublisherWrites_AdminReadsOnly()
    {
        var type = typeof(PlatformPaymentsController);
        type.GetCustomAttribute<AuthorizeAttribute>()!.Roles.Should().BeNull();
        foreach (var read in new[] { nameof(PlatformPaymentsController.GetPayments), nameof(PlatformPaymentsController.GetBankAccount) })
            type.GetMethod(read)!.GetCustomAttribute<AuthorizeAttribute>()!.Roles.Should().Be("Publisher,Admin");
        foreach (var write in new[]
        {
            nameof(PlatformPaymentsController.Approve),
            nameof(PlatformPaymentsController.Reject),
            nameof(PlatformPaymentsController.UpdateBankAccount),
        })
            type.GetMethod(write)!.GetCustomAttribute<AuthorizeAttribute>()!.Roles.Should().Be("Publisher");
    }

    [Fact]
    public void PublisherControllers_EnforcePublisherOnlyMonetization_AndAdminBr32DashboardReads()
    {
        // PublisherMonetizationController: class-level [Authorize], insights allows Publisher,Admin (BR-32),
        // SetEditionMonetization / GetManage / GetPageImage are Publisher-only (BR-33)
        typeof(PublisherMonetizationController).GetCustomAttribute<AuthorizeAttribute>()!.Roles.Should().BeNull();
        typeof(PublisherMonetizationController)
            .GetMethod(nameof(PublisherMonetizationController.GetInsights))!
            .GetCustomAttribute<AuthorizeAttribute>()!.Roles.Should().Be("Publisher,Admin");
        foreach (var methodName in new[]
        {
            nameof(PublisherMonetizationController.SetEditionMonetization),
            nameof(PublisherMonetizationController.GetManage),
            nameof(PublisherMonetizationController.GetPageImage),
        })
        {
            typeof(PublisherMonetizationController).GetMethod(methodName)!
                .GetCustomAttribute<AuthorizeAttribute>()!.Roles.Should().Be("Publisher");
        }

        // PublisherController: revenue & workspaces allow Publisher,Admin (BR-32); chapters/series are Publisher-only
        typeof(PublisherController).GetCustomAttribute<AuthorizeAttribute>()!.Roles.Should().BeNull();
        foreach (var readMethod in new[]
        {
            nameof(PublisherController.GetRevenue),
            nameof(PublisherController.GetWorkspaces),
        })
        {
            typeof(PublisherController).GetMethod(readMethod)!
                .GetCustomAttribute<AuthorizeAttribute>()!.Roles.Should().Be("Publisher,Admin");
        }
        foreach (var pubOnlyMethod in new[]
        {
            nameof(PublisherController.GetChapters),
            nameof(PublisherController.GetSeries),
            nameof(PublisherController.GetSeriesDetail),
        })
        {
            typeof(PublisherController).GetMethod(pubOnlyMethod)!
                .GetCustomAttribute<AuthorizeAttribute>()!.Roles.Should().Be("Publisher");
        }
    }

    private User Publisher(bool twoFactor)
    {
        var user = User.Create("publisher@panelforge.vn", "Phát Hành", role: SystemRole.Publisher);
        if (twoFactor)
        {
            user.SetTwoFactorSecret("JBSWY3DPEHPK3PXP");
            user.EnableTwoFactor();
        }
        _db.Users.Add(user);
        _db.SaveChanges();
        return user;
    }

    private static ITotpService Totp(long? step = 1000)
    {
        var mock = new Mock<ITotpService>();
        mock.Setup(t => t.MatchTimeStep(It.IsAny<string>(), "123456")).Returns(step);
        return mock.Object;
    }

    private Task<PanelForge.Application.Common.Result<PlatformBankAccountDto>> Update(Guid userId, string code = "123456", string accountNo = "9988776655") =>
        new UpdatePlatformBankAccountCommandHandler(_db, Totp())
            .Handle(new UpdatePlatformBankAccountCommand(userId, "970436", "Vietcombank", accountNo, "panelforge jsc", code), default);

    [Fact]
    public async Task GetPlatformBankAccount_WithoutRecord_FallsBackToDefaultBank()
    {
        var configured = new DefaultBankSettings { Bin = "970436", Name = "Vietcombank", AccountNo = "1029384756", AccountName = "PANELFORGE JSC" };

        var dto = (await new GetPlatformBankAccountQueryHandler(_db, configured).Handle(new GetPlatformBankAccountQuery(), default)).Value!;

        dto.Current.AccountNo.Should().Be("1029384756");
        dto.Current.IsConfigured.Should().BeFalse();
        dto.History.Should().BeEmpty();
    }

    [Fact]
    public async Task UpdatePlatformBankAccount_WithValidCode_AppendsRecordAndAudit()
    {
        var publisher = Publisher(twoFactor: true);

        (await Update(publisher.Id)).IsSuccess.Should().BeTrue();
        (await new UpdatePlatformBankAccountCommandHandler(_db, Totp(step: 1001))
            .Handle(new UpdatePlatformBankAccountCommand(publisher.Id, "970422", "MB Bank", "0011223344", "PANELFORGE JSC", "123456"), default))
            .IsSuccess.Should().BeTrue();

        (await _db.PlatformBankAccounts.CountAsync()).Should().Be(2, "append-only: đổi tài khoản thêm bản ghi mới, không ghi đè");
        var dto = (await new GetPlatformBankAccountQueryHandler(_db, new DefaultBankSettings()).Handle(new GetPlatformBankAccountQuery(), default)).Value!;
        dto.Current.AccountNo.Should().Be("0011223344");
        dto.Current.IsConfigured.Should().BeTrue();
        dto.History.Should().HaveCount(2);

        var audits = await _db.AuditLogs.Where(a => a.Action == "PLATFORM_BANK_ACCOUNT_CHANGED").ToListAsync();
        audits.Should().HaveCount(2);
        audits.Should().OnlyContain(a => a.UserId == publisher.Id);
    }

    [Fact]
    public async Task UpdatePlatformBankAccount_WithoutTwoFactor_IsForbidden()
    {
        var publisher = Publisher(twoFactor: false);

        var result = await Update(publisher.Id);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ResultErrorCodes.Forbidden);
        (await _db.PlatformBankAccounts.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task UpdatePlatformBankAccount_WithWrongOrReusedCode_IsRejected()
    {
        var publisher = Publisher(twoFactor: true);

        (await Update(publisher.Id, code: "000000")).IsSuccess.Should().BeFalse();
        (await Update(publisher.Id)).IsSuccess.Should().BeTrue();
        (await Update(publisher.Id, accountNo: "5566778899")).IsSuccess.Should().BeFalse("mã TOTP đã dùng không dùng lại được");

        (await _db.PlatformBankAccounts.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task UpdatePlatformBankAccount_WithInvalidAccountNo_IsValidationError()
    {
        var publisher = Publisher(twoFactor: true);

        var result = await Update(publisher.Id, accountNo: "12-34");

        result.ErrorCode.Should().Be(ResultErrorCodes.Validation);
        (await _db.PlatformBankAccounts.AnyAsync()).Should().BeFalse();
    }
}
