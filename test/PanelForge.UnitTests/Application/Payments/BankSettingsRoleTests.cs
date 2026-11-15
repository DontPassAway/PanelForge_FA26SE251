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
    public void WorkspacePaymentsController_EnforcesPublisherAndAdminRoleBoundaries()
    {
        // 1. Controller chỉ cho phép Publisher và Admin (Producer bị 403 Forbidden)
        var controllerAttr = typeof(WorkspacePaymentsController).GetCustomAttribute<AuthorizeAttribute>();
        controllerAttr.Should().NotBeNull();
        controllerAttr!.Roles.Should().Be("Publisher,Admin");

        // 2. Duyệt thanh toán VietQR: Dành riêng cho Admin, Publisher KHÔNG được duyệt (403 Forbidden)
        var approveMethod = typeof(WorkspacePaymentsController).GetMethod(nameof(WorkspacePaymentsController.ApprovePayment));
        var approveAttr = approveMethod!.GetCustomAttribute<AuthorizeAttribute>();
        approveAttr.Should().NotBeNull();
        approveAttr!.Roles.Should().Be("Admin");

        // 3. Từ chối thanh toán VietQR: Dành riêng cho Admin, Publisher KHÔNG được từ chối (403 Forbidden)
        var rejectMethod = typeof(WorkspacePaymentsController).GetMethod(nameof(WorkspacePaymentsController.RejectPayment));
        var rejectAttr = rejectMethod!.GetCustomAttribute<AuthorizeAttribute>();
        rejectAttr.Should().NotBeNull();
        rejectAttr!.Roles.Should().Be("Admin");

        // 4. Đọc & Cập nhật ngân hàng: Kế thừa [Authorize(Roles = "Publisher,Admin")] từ Controller level
        var getBankMethod = typeof(WorkspacePaymentsController).GetMethod(nameof(WorkspacePaymentsController.GetBankSettings));
        getBankMethod.Should().NotBeNull();
        getBankMethod!.GetCustomAttribute<AuthorizeAttribute>().Should().BeNull("kế thừa controller level Publisher,Admin");

        var updateBankMethod = typeof(WorkspacePaymentsController).GetMethod(nameof(WorkspacePaymentsController.UpdateBankSettings));
        updateBankMethod.Should().NotBeNull();
        updateBankMethod!.GetCustomAttribute<AuthorizeAttribute>().Should().BeNull("kế thừa controller level Publisher,Admin");
    }

    [Fact]
    public async Task GetWorkspaceBankSettings_ShouldReturnConfiguredBankInfo()
    {
        var handler = new GetWorkspaceBankSettingsQueryHandler(_db);
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
