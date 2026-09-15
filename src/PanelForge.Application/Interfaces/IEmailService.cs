namespace PanelForge.Application.Interfaces;

public interface IEmailService
{
    Task SendEmailAsync(string toEmail, string subject, string htmlBody, CancellationToken cancellationToken = default);
    Task SendEmailVerificationAsync(string toEmail, string token, string recipientName, CancellationToken cancellationToken = default);
    Task SendPasswordResetEmailAsync(string toEmail, string token, string recipientName, CancellationToken cancellationToken = default);
}
