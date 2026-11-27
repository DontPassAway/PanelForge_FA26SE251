using PanelForge.Application.Features.Payments.Models;
using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using PanelForge.API.Controllers;
using PanelForge.Application.Features.Payments.Commands.UpdateWorkspaceBankSettings;
using PanelForge.Application.Features.Payments.Queries.GetWorkspaceBankSettings;
using PanelForge.Domain.Entities.Workspaces;
using PanelForge.Infrastructure.Persistence;
using PanelForge.UnitTests.Common;
using Xunit;

namespace PanelForge.UnitTests.Application.Payments;

/// <summary>
/// Quyết định 2026-11-15: Publisher cấu hình tài khoản ngân hàng nhận thanh toán (không duyệt VietQR).
/// Producer không còn quyền cấu hình tài khoản ngân hàng. Duyệt/từ chối VietQR dành riêng cho Admin.
/// </summary>
public class BankSettingsRoleTests
{
    private readonly PanelForgeDbContext _db = InMemoryDb.Create();
    private readonly Guid _producerId = Guid.NewGuid();
    private readonly StudioWorkspace _studio;

    public BankSettingsRoleTests()
    {
        _studio = StudioWorkspace.Create("Studio Moon", _producerId);
        _studio.UpdateBankInfo("970422", "MB Bank", "123456789", "NGUYEN VAN A");
        _db.StudioWorkspaces.Add(_studio);
        _db.SaveChanges();
    }

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

        // Q13 / BR-33: VietQR approve, reject, and bank settings are strictly Publisher-only (Admin -> 403)
        foreach (var methodName in new[]
        {
            nameof(WorkspacePaymentsController.ApprovePayment),
            nameof(WorkspacePaymentsController.RejectPayment),
            nameof(WorkspacePaymentsController.GetBankSettings),
            nameof(WorkspacePaymentsController.UpdateBankSettings),
        })
        {
            var attr = typeof(WorkspacePaymentsController).GetMethod(methodName)!.GetCustomAttribute<AuthorizeAttribute>();
            attr.Should().NotBeNull($"{methodName} must declare [Authorize(Roles = \"Publisher\")]");
            attr!.Roles.Should().Be("Publisher");
        }
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

    [Fact]
    public async Task GetWorkspaceBankSettings_ShouldReturnConfiguredBankInfo()
    {
        var handler = new GetWorkspaceBankSettingsQueryHandler(_db, new DefaultBankSettings());
        var result = await handler.Handle(new GetWorkspaceBankSettingsQuery(_studio.Id), default);

        result.IsSuccess.Should().BeTrue();
        result.Value!.BankBin.Should().Be("970422");
        result.Value.BankName.Should().Be("MB Bank");
        result.Value.BankAccountNo.Should().Be("123456789");
        result.Value.BankAccountName.Should().Be("NGUYEN VAN A");
    }

    [Fact]
    public async Task UpdateWorkspaceBankSettings_ShouldUpdateStudioBankInfoSuccessfully()
    {
        var handler = new UpdateWorkspaceBankSettingsCommandHandler(_db);
        var command = new UpdateWorkspaceBankSettingsCommand(
            _studio.Id,
            BankBin: "970436",
            BankName: "Vietcombank",
            BankAccountNo: "9988776655",
            BankAccountName: "TRAN VAN B"
        );

        var result = await handler.Handle(command, default);

        result.IsSuccess.Should().BeTrue();
        result.Value!.BankBin.Should().Be("970436");
        result.Value.BankName.Should().Be("Vietcombank");
        result.Value.BankAccountNo.Should().Be("9988776655");
        result.Value.BankAccountName.Should().Be("TRAN VAN B");

        var updatedWs = await _db.StudioWorkspaces.FindAsync(_studio.Id);
        updatedWs!.BankBin.Should().Be("970436");
        updatedWs.BankName.Should().Be("Vietcombank");
        updatedWs.BankAccountNo.Should().Be("9988776655");
        updatedWs.BankAccountName.Should().Be("TRAN VAN B");
    }
}
