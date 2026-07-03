// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#nullable disable

using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using ExpenseTracker.Models.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Areas.Identity.Pages.Account
{
    [AllowAnonymous]
    public class ForgotPasswordModel : PageModel
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IEmailSender _emailSender;

        public ForgotPasswordModel(UserManager<ApplicationUser> userManager, IEmailSender emailSender)
        {
            _userManager = userManager;
            _emailSender = emailSender;
        }

        [BindProperty]
        public InputModel Input { get; set; }

        public class InputModel
        {
            [Required]
            [Display(Name = "Email or User ID")]
            public string EmailOrUserId { get; set; }
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid)
            {
                return Page();
            }

            var identifier = Input.EmailOrUserId.Trim();

            // Resolve by email (contains '@') or by DisplayUserId — mirrors the login form,
            // so people can recover with whichever handle they remember.
            var user = identifier.Contains('@')
                ? await _userManager.FindByEmailAsync(identifier)
                : await _userManager.Users
                    .FirstOrDefaultAsync(u => u.DisplayUserId == identifier);

            var email = user is null ? null : await _userManager.GetEmailAsync(user);

            // Always land on the same confirmation regardless of whether the account (or
            // its email) exists, so this form can't be used to enumerate registered users.
            //
            // NOTE: intentionally NOT gating on IsEmailConfirmedAsync. Finoma registers
            // users with RequireConfirmedAccount = false, so no account is ever "confirmed";
            // the stock template's confirmation check would silently block every reset.
            if (user is null || string.IsNullOrWhiteSpace(email))
            {
                return RedirectToPage("./ForgotPasswordConfirmation");
            }

            var code = await _userManager.GeneratePasswordResetTokenAsync(user);
            code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));
            var callbackUrl = Url.Page(
                "/Account/ResetPassword",
                pageHandler: null,
                values: new { area = "Identity", code },
                protocol: Request.Scheme);

            await _emailSender.SendEmailAsync(
                email,
                "Reset your Finoma password",
                BuildEmailBody(HtmlEncoder.Default.Encode(callbackUrl)));

            return RedirectToPage("./ForgotPasswordConfirmation");
        }

        // Branded, email-client-safe HTML (inline styles only — no <style> block, no external
        // assets). Mirrors the Ledger palette (paper/ink + crimson stamp accent).
        private static string BuildEmailBody(string resetUrl) => $"""
            <div style="margin:0;padding:24px;background:#f4f1ea;font-family:'Segoe UI',Arial,sans-serif;color:#1a1a1a;">
              <div style="max-width:520px;margin:0 auto;background:#ffffff;border:1px solid #e7e2d6;border-radius:10px;overflow:hidden;">
                <div style="padding:22px 28px;border-bottom:3px double #1a1a1a;">
                  <span style="display:inline-block;width:26px;height:26px;line-height:26px;text-align:center;background:#C20E3A;color:#fff;border-radius:50%;font-weight:700;vertical-align:middle;">&#8377;</span>
                  <span style="font-size:20px;font-weight:700;letter-spacing:.5px;margin-left:8px;vertical-align:middle;">Finoma</span>
                </div>
                <div style="padding:28px;">
                  <p style="font-size:11px;letter-spacing:1.5px;text-transform:uppercase;color:#C20E3A;margin:0 0 10px;">Account ledger &middot; Password reset</p>
                  <h1 style="font-size:22px;margin:0 0 14px;">Reset your password</h1>
                  <p style="font-size:15px;line-height:1.55;margin:0 0 22px;color:#3a3a3a;">
                    We received a request to reset the password for your Finoma account.
                    Click the button below to choose a new one. This link is single-use.
                  </p>
                  <p style="margin:0 0 26px;">
                    <a href="{resetUrl}" style="display:inline-block;background:#C20E3A;color:#ffffff;text-decoration:none;font-weight:600;font-size:15px;padding:12px 26px;border-radius:8px;">Reset password</a>
                  </p>
                  <p style="font-size:13px;line-height:1.5;margin:0 0 6px;color:#6b6b6b;">
                    If the button doesn't work, copy and paste this link into your browser:
                  </p>
                  <p style="font-size:12px;word-break:break-all;margin:0 0 24px;"><a href="{resetUrl}" style="color:#C20E3A;">{resetUrl}</a></p>
                  <p style="font-size:13px;line-height:1.5;margin:0;color:#6b6b6b;border-top:1px solid #ececec;padding-top:16px;">
                    Didn't ask for this? You can safely ignore this email — your password stays unchanged.
                  </p>
                </div>
              </div>
              <p style="max-width:520px;margin:16px auto 0;text-align:center;font-size:11px;color:#9a9a9a;">&copy; Finoma &middot; Your personal finance companion</p>
            </div>
            """;
    }
}
