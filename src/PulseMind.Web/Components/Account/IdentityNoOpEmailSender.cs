using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using PulseMind.Core.Data;

namespace PulseMind.Web.Components.Account;

// Remove the "else if (EmailSender is IdentityNoOpEmailSender)" block from RegisterConfirmation.razor after updating with a real implementation.
internal sealed class IdentityNoOpEmailSender : IEmailSender<ApplicationUser>
{
    private readonly IEmailSender emailSender = new NoOpEmailSender();

    public Task SendConfirmationLinkAsync(ApplicationUser user, string email, string confirmationLink) =>
        emailSender.SendEmailAsync(email, "メールアドレスの確認", $"<a href='{confirmationLink}'>こちらをクリック</a>して、Pulse &amp; Mind Study のアカウントを確認してください。");

    public Task SendPasswordResetLinkAsync(ApplicationUser user, string email, string resetLink) =>
        emailSender.SendEmailAsync(email, "パスワードの再設定", $"<a href='{resetLink}'>こちらをクリック</a>して、パスワードを再設定してください。");

    public Task SendPasswordResetCodeAsync(ApplicationUser user, string email, string resetCode) =>
        emailSender.SendEmailAsync(email, "パスワードの再設定", $"次のコードを入力して、パスワードを再設定してください: {resetCode}");
}
