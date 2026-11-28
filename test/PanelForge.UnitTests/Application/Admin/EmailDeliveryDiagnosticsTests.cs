using System.Net;
using System.Net.Mail;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.Admin.Models;
using PanelForge.Application.Features.Admin.Queries.GetEmailDeliveryLogs;
using PanelForge.Application.Features.Admin.Queries.GetSystemHealth;
using PanelForge.Domain.Entities.Platform;
using PanelForge.Infrastructure.ExternalServices.Email;
using PanelForge.Infrastructure.Persistence;
using PanelForge.UnitTests.Common;
using Xunit;

namespace PanelForge.UnitTests.Application.Admin;

/// <summary>
/// Chẩn đoán gửi email: lỗi SMTP được phân loại để Admin biết sửa gì, mỗi lần gửi đều ghi nhật ký (không lưu nội dung OTP),
/// System Health đếm thư lỗi 24h.
/// </summary>
public class EmailDeliveryDiagnosticsTests
{
    [Fact]
    public void Classify_MapsSmtpErrorsToReasons()
    {
        SmtpFailureClassifier.Classify(new SmtpException((SmtpStatusCode)535, "5.7.8 Username and Password not accepted"))
            .Should().Be(EmailFailureReasons.AuthFailed);
        SmtpFailureClassifier.Classify(new SmtpException("Failure sending mail.", new SocketException((int)SocketError.ConnectionRefused)))
            .Should().Be(EmailFailureReasons.ConnectionFailed);
        SmtpFailureClassifier.Classify(new SmtpException("Failure sending mail.", new AuthenticationException("The remote certificate is invalid")))
            .Should().Be(EmailFailureReasons.TlsFailed);
        SmtpFailureClassifier.Classify(new SmtpException("Server does not support secure connections."))
            .Should().Be(EmailFailureReasons.TlsFailed);
        SmtpFailureClassifier.Classify(new OperationCanceledException())
            .Should().Be(EmailFailureReasons.Timeout);
        SmtpFailureClassifier.Classify(new SmtpFailedRecipientException(SmtpStatusCode.MailboxUnavailable, "x@y.z"))
            .Should().Be(EmailFailureReasons.RecipientRejected);
        SmtpFailureClassifier.Classify(new FormatException("bad address"))
            .Should().Be(EmailFailureReasons.InvalidAddress);
    }

    [Fact]
    public void Describe_JoinsInnerMessages()
        => SmtpFailureClassifier.Describe(new SmtpException("Failure sending mail.", new IOException("Connection reset")))
            .Should().Be("Failure sending mail. → Connection reset");

    [Fact]
    public async Task Send_WithoutSmtpConfig_FailsAsNotConfigured_AndLogsWithoutBody()
    {
        var (service, db) = CreateService(new Dictionary<string, string?>());

        var sent = await service.SendEmailVerificationAsync("reader@panelforge.vn", "123456", "Reader", TimeSpan.FromMinutes(10));

        sent.Should().BeFalse();
        var log = db.EmailDeliveryLogs.Single();
        log.Succeeded.Should().BeFalse();
        log.FailureReason.Should().Be(EmailFailureReasons.NotConfigured);
        log.MailType.Should().Be(EmailMailTypes.Verification);
        log.ToEmail.Should().Be("reader@panelforge.vn");
        log.Subject.Should().NotContain("123456");
        (await service.GetConfigurationStatusAsync()).Should()
            .Be(new EmailConfigurationStatus(false, "config", "smtp.gmail.com", 587, EmailFailureReasons.NotConfigured));
    }

    [Fact]
    public async Task SendTest_ClosedPort_ReportsConnectionFailed()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var (service, db) = CreateService(SmtpConfig(port));

        var result = await service.SendTestEmailAsync("admin@panelforge.vn");

        result.Succeeded.Should().BeFalse();
        result.FailureReason.Should().Be(EmailFailureReasons.ConnectionFailed);
        result.Detail.Should().NotBeNullOrWhiteSpace();
        var log = db.EmailDeliveryLogs.Single();
        log.MailType.Should().Be(EmailMailTypes.Test);
        log.SmtpPort.Should().Be(port);
        log.ErrorDetail.Should().Be(result.Detail);
    }

    [Fact]
    public async Task SendTest_ServerWithoutStartTls_ReportsTlsFailed()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var server = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            await using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII);
            await using var writer = new StreamWriter(stream, Encoding.ASCII) { NewLine = "\r\n", AutoFlush = true };
            await writer.WriteLineAsync("220 fake ESMTP");
            await reader.ReadLineAsync(); // EHLO
            await writer.WriteLineAsync("250 fake");
            await reader.ReadLineAsync(); // QUIT hoặc đóng kết nối
        });
        var (service, _) = CreateService(SmtpConfig(port));

        var result = await service.SendTestEmailAsync("admin@panelforge.vn");

        result.FailureReason.Should().Be(EmailFailureReasons.TlsFailed);
        await Task.WhenAny(server, Task.Delay(2000));
    }

    [Fact]
    public async Task EmailLogs_FiltersAndCountsFailuresLast24h()
    {
        await using var db = InMemoryDb.Create();
        db.EmailDeliveryLogs.AddRange(
            EmailDeliveryLog.Create("a@x.vn", EmailMailTypes.Verification, "s", true, null, null, "smtp", 587),
            EmailDeliveryLog.Create("b@x.vn", EmailMailTypes.WorkspaceInvitation, "s", false, EmailFailureReasons.AuthFailed, "535", "smtp", 587),
            EmailDeliveryLog.Create("c@x.vn", EmailMailTypes.WorkspaceInvitation, "s", false, EmailFailureReasons.Timeout, "t", "smtp", 587));
        await db.SaveChangesAsync();
        var handler = new GetEmailDeliveryLogsQueryHandler(db);

        var failed = (await handler.Handle(new GetEmailDeliveryLogsQuery(Succeeded: false), CancellationToken.None)).Value!;
        failed.TotalCount.Should().Be(2);
        failed.FailedLast24h.Should().Be(2);

        var search = (await handler.Handle(new GetEmailDeliveryLogsQuery(Search: "B@X"), CancellationToken.None)).Value!;
        search.Items.Should().ContainSingle().Which.FailureReason.Should().Be(EmailFailureReasons.AuthFailed);

        var type = (await handler.Handle(new GetEmailDeliveryLogsQuery(MailType: EmailMailTypes.Verification), CancellationToken.None)).Value!;
        type.Items.Should().ContainSingle().Which.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task SystemHealth_CountsFailedEmailsLast24h()
    {
        await using var db = InMemoryDb.Create();
        db.EmailDeliveryLogs.Add(EmailDeliveryLog.Create("b@x.vn", EmailMailTypes.PasswordReset, "s", false, EmailFailureReasons.AuthFailed, null, "smtp", 587));
        await db.SaveChangesAsync();
        var probe = new Mock<ISystemHealthProbe>();
        probe.Setup(p => p.CheckDependenciesAsync(It.IsAny<CancellationToken>()))
             .ReturnsAsync([new DependencyHealthDto("database", HealthStatus.Healthy, "PostgreSQL", 1, null, null)]);

        var dto = (await new GetSystemHealthQueryHandler(db, probe.Object).Handle(new GetSystemHealthQuery(), CancellationToken.None)).Value!;

        var signal = dto.Signals.Single(s => s.Key == "emailsFailed24h");
        signal.Count.Should().Be(1);
        signal.Warning.Should().BeTrue();
    }

    private static Dictionary<string, string?> SmtpConfig(int port) => new()
    {
        ["EmailConfiguration:SmtpServer"] = "127.0.0.1",
        ["EmailConfiguration:SmtpPort"] = port.ToString(),
        ["EmailConfiguration:SenderEmail"] = "noreply@panelforge.vn",
        ["EmailConfiguration:Password"] = "app-password",
    };

    private static (EmailService Service, PanelForgeDbContext Db) CreateService(Dictionary<string, string?> config)
    {
        var dbName = Guid.NewGuid().ToString();
        var services = new ServiceCollection()
            .AddScoped<IPanelForgeDbContext>(_ => InMemoryDb.Create(databaseName: dbName))
            .BuildServiceProvider();
        var requestDb = InMemoryDb.Create(databaseName: dbName);
        var service = new EmailService(
            new ConfigurationBuilder().AddInMemoryCollection(config).Build(),
            NullLogger<EmailService>.Instance,
            requestDb,
            Mock.Of<IAiKeyProtector>(),
            services.GetRequiredService<IServiceScopeFactory>());
        return (service, InMemoryDb.Create(databaseName: dbName));
    }
}
