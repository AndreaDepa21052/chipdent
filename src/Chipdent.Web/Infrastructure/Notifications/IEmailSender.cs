namespace Chipdent.Web.Infrastructure.Notifications;

public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken ct = default);
}

public record EmailMessage(
    string To,
    string Subject,
    string HtmlBody,
    string? TextBody = null,
    string? ReplyTo = null);

/// <summary>
/// Implementazione no-op per ambiente di sviluppo: logga e basta.
/// In produzione collegare un MailKit/SmtpClient/SendGrid/etc.
/// </summary>
public class LogOnlyEmailSender : IEmailSender
{
    private readonly ILogger<LogOnlyEmailSender> _log;
    public LogOnlyEmailSender(ILogger<LogOnlyEmailSender> log) => _log = log;

    public Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        _log.LogInformation("📧 [DEV-MAIL] To={To}  Subject={Subject}  Length={Len}",
            message.To, message.Subject, message.HtmlBody.Length);
        return Task.CompletedTask;
    }
}
