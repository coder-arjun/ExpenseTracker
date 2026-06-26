using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace ExpenseTracker.Services
{
    /// <summary>Bound from the "Email" config section (appsettings / env vars).</summary>
    public class EmailOptions
    {
        public bool Enabled { get; set; } = false;
        public string Host { get; set; } = "smtp.gmail.com";
        public int Port { get; set; } = 587;
        public string User { get; set; } = "";        // Gmail address
        public string Password { get; set; } = "";    // Gmail App Password (16 chars)
        public string FromAddress { get; set; } = ""; // defaults to User if blank
        public string FromName { get; set; } = "Finoma";
    }

    /// <summary>
    /// Sends mail (optionally with a single PDF attachment) over SMTP using MailKit.
    /// Configured for Gmail (smtp.gmail.com:587, STARTTLS, App Password) but works
    /// with any SMTP host. No-ops loudly if not configured so callers can branch.
    /// </summary>
    public class EmailSender
    {
        private readonly EmailOptions _o;
        private readonly ILogger<EmailSender> _log;

        public EmailSender(IOptions<EmailOptions> o, ILogger<EmailSender> log)
        {
            _o = o.Value;
            _log = log;
        }

        public bool IsConfigured =>
            _o.Enabled && !string.IsNullOrWhiteSpace(_o.User) && !string.IsNullOrWhiteSpace(_o.Password);

        public async Task SendAsync(
            string toEmail, string? toName, string subject, string htmlBody,
            byte[]? attachment = null, string? attachmentName = null,
            CancellationToken ct = default)
        {
            if (!IsConfigured)
                throw new InvalidOperationException("Email is not configured. Set the Email section (Enabled, User, Password).");

            var msg = new MimeMessage();
            var from = string.IsNullOrWhiteSpace(_o.FromAddress) ? _o.User : _o.FromAddress;
            msg.From.Add(new MailboxAddress(_o.FromName, from));
            msg.To.Add(new MailboxAddress(string.IsNullOrWhiteSpace(toName) ? toEmail : toName, toEmail));
            msg.Subject = subject;

            var body = new BodyBuilder { HtmlBody = htmlBody };
            if (attachment is { Length: > 0 })
                body.Attachments.Add(attachmentName ?? "statement.pdf", attachment, new ContentType("application", "pdf"));
            msg.Body = body.ToMessageBody();

            using var client = new SmtpClient();
            await client.ConnectAsync(_o.Host, _o.Port, SecureSocketOptions.StartTls, ct);
            await client.AuthenticateAsync(_o.User, _o.Password, ct);
            await client.SendAsync(msg, ct);
            await client.DisconnectAsync(true, ct);
            _log.LogInformation("Email sent to {To} (subject: {Subject})", toEmail, subject);
        }
    }
}
