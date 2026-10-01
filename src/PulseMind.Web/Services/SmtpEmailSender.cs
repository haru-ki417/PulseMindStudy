using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using MimeKit;
using PulseMind.Core.Data;

namespace PulseMind.Web.Services;

/// <summary>メール送信の設定（appsettings の "Smtp"。パスワードは Key Vault や App Service の設定で渡す）</summary>
public sealed class SmtpOptions
{
    public string? Host { get; set; }
    public int Port { get; set; } = 587;
    public string? UserName { get; set; }
    public string? Password { get; set; }
    public string From { get; set; } = "";
    public string FromName { get; set; } = "Pulse & Mind Study";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(From);
}

/// <summary>確認メール・パスワード再設定メールを SMTP で送る（日本語の文面）</summary>
public sealed partial class SmtpEmailSender(IOptions<SmtpOptions> options, ILogger<SmtpEmailSender> logger) : IEmailSender<ApplicationUser>
{
    public Task SendConfirmationLinkAsync(ApplicationUser user, string email, string confirmationLink) =>
        SendAsync(email, "メールアドレスの確認", $"""
            <p>Pulse &amp; Mind Study にご登録いただきありがとうございます。</p>
            <p>次のリンクを開いて、メールアドレスの確認を完了してください。</p>
            <p><a href="{confirmationLink}">メールアドレスを確認する</a></p>
            <p style="color:#666">このメールに心当たりがない場合は、何もせずに削除してください。</p>
            """);

    public Task SendPasswordResetLinkAsync(ApplicationUser user, string email, string resetLink) =>
        SendAsync(email, "パスワードの再設定", $"""
            <p>パスワードの再設定を受け付けました。次のリンクから新しいパスワードを設定してください。</p>
            <p><a href="{resetLink}">パスワードを再設定する</a></p>
            <p style="color:#666">ご自身で手続きしていない場合は、このメールを削除してください。パスワードは変更されません。</p>
            """);

    public Task SendPasswordResetCodeAsync(ApplicationUser user, string email, string resetCode) =>
        SendAsync(email, "パスワード再設定のコード", $"""
            <p>パスワードを再設定するためのコードです。</p>
            <p style="font-size:20px;font-weight:bold;letter-spacing:2px">{System.Net.WebUtility.HtmlEncode(resetCode)}</p>
            <p style="color:#666">ご自身で手続きしていない場合は、このメールを削除してください。</p>
            """);

    private async Task SendAsync(string to, string subject, string htmlBody)
    {
        var o = options.Value;
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(o.FromName, o.From));
        message.To.Add(MailboxAddress.Parse(to));
        message.Subject = $"【Pulse & Mind Study】{subject}";
        message.Body = new BodyBuilder
        {
            HtmlBody = $"""<div style="font-family:sans-serif;line-height:1.7;color:#18202f">{htmlBody}<hr style="border:none;border-top:1px solid #ddd"><p style="color:#888;font-size:12px">Pulse &amp; Mind Study（このメールは送信専用です）</p></div>""",
        }.ToMessageBody();

        using var client = new SmtpClient();
        await client.ConnectAsync(o.Host!, o.Port, o.Port == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls);
        if (!string.IsNullOrEmpty(o.UserName)) await client.AuthenticateAsync(o.UserName, o.Password ?? "");
        await client.SendAsync(message);
        await client.DisconnectAsync(true);
        LogSent(subject); // 宛先のメールアドレスはログに残さない
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "メールを送信しました: {Subject}")]
    private partial void LogSent(string subject);
}
