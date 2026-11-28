using FluentAssertions;
using Moq;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.Admin.Queries.GetEmailDeliveryLogs;
using PanelForge.Application.Features.Admin.Queries.GetEmailHealth;
using PanelForge.Domain.Entities.Platform;
using PanelForge.Infrastructure.Persistence;
using PanelForge.UnitTests.Common;
using Xunit;

namespace PanelForge.UnitTests.Application.Admin;

/// <summary>
/// Nhật ký gửi email lọc theo khoảng thời gian; GET email-health (chỉ đọc) cảnh báo khi chưa cấu hình SMTP
/// hoặc lần gửi gần nhất lỗi, không cảnh báo khi bình thường.
/// </summary>
public class EmailHealthAndLogRangeTests
{
    private readonly PanelForgeDbContext _db = InMemoryDb.Create();

    private async Task LogAsync(bool succeeded, DateTime at, string? reason = null)
    {
        var log = EmailDeliveryLog.Create("u@panelforge.vn", EmailMailTypes.PasswordReset, "s", succeeded,
            succeeded ? null : reason ?? EmailFailureReasons.AuthFailed, null, "smtp.gmail.com", 587);
        _db.EmailDeliveryLogs.Add(log);
        await _db.SaveChangesAsync();
        log.CreatedAt = at; // SaveChanges đặt CreatedAt = now khi thêm mới
        await _db.SaveChangesAsync();
    }

    private async Task<AdminEmailHealthDto> Health(bool configured, string? problem = null)
    {
        var email = new Mock<IEmailService>();
        email.Setup(e => e.GetConfigurationStatusAsync(It.IsAny<CancellationToken>()))
             .ReturnsAsync(new EmailConfigurationStatus(configured, "app", "smtp.gmail.com", 587, problem));
        return (await new GetEmailHealthQueryHandler(_db, email.Object).Handle(new GetEmailHealthQuery(), CancellationToken.None)).Value!;
    }

    [Fact]
    public async Task Logs_FilterByDateRange_AndRejectInvertedRange()
    {
        await LogAsync(true, new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc));
        await LogAsync(false, new DateTime(2026, 11, 20, 0, 0, 0, DateTimeKind.Utc));
        await LogAsync(true, new DateTime(2026, 11, 25, 0, 0, 0, DateTimeKind.Utc));
        var handler = new GetEmailDeliveryLogsQueryHandler(_db);

        var ranged = await handler.Handle(new GetEmailDeliveryLogsQuery(
            From: new DateTime(2026, 11, 10, 0, 0, 0, DateTimeKind.Utc),
            To: new DateTime(2026, 11, 21, 0, 0, 0, DateTimeKind.Utc)), CancellationToken.None);
        ranged.Value!.TotalCount.Should().Be(1);
        ranged.Value.Items.Single().Succeeded.Should().BeFalse();

        var inverted = await handler.Handle(new GetEmailDeliveryLogsQuery(
            From: new DateTime(2026, 11, 21, 0, 0, 0, DateTimeKind.Utc),
            To: new DateTime(2026, 11, 10, 0, 0, 0, DateTimeKind.Utc)), CancellationToken.None);
        inverted.ErrorCode.Should().Be(ResultErrorCodes.Validation);
    }

    [Fact]
    public async Task Health_NotConfigured_Warns()
    {
        var health = await Health(configured: false, problem: "PASSWORD_UNREADABLE");

        health.Status.Should().Be(EmailHealthStatus.NotConfigured);
        health.Problem.Should().Be("PASSWORD_UNREADABLE");
    }

    [Fact]
    public async Task Health_LastSendFailed_Warns_WithReasonAndRecentCounts()
    {
        var now = DateTime.UtcNow;
        await LogAsync(true, now.AddHours(-30));   // ngoài 24 giờ
        await LogAsync(true, now.AddHours(-2));
        await LogAsync(false, now.AddMinutes(-5), EmailFailureReasons.AuthFailed);

        var health = await Health(configured: true);

        health.Status.Should().Be(EmailHealthStatus.LastSendFailed);
        health.LastFailureReason.Should().Be(EmailFailureReasons.AuthFailed);
        health.RecentTotal.Should().Be(2);
        health.RecentFailed.Should().Be(1);
    }

    [Fact]
    public async Task Health_IsOk_WhenConfigured_AndLatestSendSucceeded()
    {
        var now = DateTime.UtcNow;
        await LogAsync(false, now.AddMinutes(-10));
        await LogAsync(true, now.AddMinutes(-1));

        var health = await Health(configured: true);

        health.Status.Should().Be(EmailHealthStatus.Ok);
        health.LastFailureReason.Should().BeNull();
    }
}
