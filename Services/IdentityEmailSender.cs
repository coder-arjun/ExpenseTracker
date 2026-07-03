using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity.UI.Services;

namespace ExpenseTracker.Services
{
    /// <summary>
    /// Bridges ASP.NET Identity's <see cref="IEmailSender"/> — used by the account
    /// email-confirmation link and the Forgot/Reset-password flow — to Finoma's
    /// MailKit-based <see cref="EmailSender"/> (the same SMTP path that already sends
    /// monthly statements and DB backups).
    ///
    /// Without this registration Identity resolves the framework's built-in
    /// <c>NoOpEmailSender</c>, which silently discards every message, so password-reset
    /// links never arrive. No-ops with a warning (rather than throwing) when SMTP is
    /// not configured, so the reset flow degrades gracefully in dev / unconfigured envs.
    /// </summary>
    public class IdentityEmailSender : IEmailSender
    {
        private readonly EmailSender _mail;
        private readonly ILogger<IdentityEmailSender> _log;
        private readonly IHostEnvironment _env;

        public IdentityEmailSender(EmailSender mail, ILogger<IdentityEmailSender> log, IHostEnvironment env)
        {
            _mail = mail;
            _log = log;
            _env = env;
        }

        public async Task SendEmailAsync(string email, string subject, string htmlMessage)
        {
            if (!_mail.IsConfigured)
            {
                _log.LogWarning(
                    "Identity email '{Subject}' to {Email} was NOT sent — SMTP is not configured (set Email:Enabled/User/Password).",
                    subject, email);

                // Development convenience: surface the action link (e.g. the password-reset
                // URL) in the logs so the flow can be exercised locally without a mail server.
                // Guarded to Development — never leaks links in production.
                if (_env.IsDevelopment())
                {
                    var href = Regex.Match(htmlMessage, "href=[\"'](?<u>[^\"']+)[\"']").Groups["u"].Value;
                    if (!string.IsNullOrEmpty(href))
                        _log.LogWarning("[DEV] Action link for '{Subject}': {Link}", subject, System.Net.WebUtility.HtmlDecode(href));
                }
                return;
            }

            await _mail.SendAsync(email, toName: null, subject: subject, htmlBody: htmlMessage);
        }
    }
}
