using System.Net;
using System.Net.Mail;
using System.Text;
using ApprovalService.Application.Notifications;
using ApprovalService.Domain.Notifications;
using Microsoft.Extensions.Options;

namespace ApprovalService.Infrastructure.Notifications;

/// <summary>
/// 使用 .NET 内置 SMTP 客户端发送纯文本提醒；邮件链接仍要求审批中心登录。
/// </summary>
/// <param name="options">部署提供的 SMTP 配置。</param>
public sealed class SmtpNotificationChannel(IOptions<SmtpNotificationOptions> options) : INotificationChannel
{
    /// <inheritdoc />
    public string Code => NotificationChannelCodes.Email;

    /// <inheritdoc />
    public async Task SendAsync(NotificationMessage message, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.Host) || settings.Port is < 1 or > 65535 ||
            string.IsNullOrWhiteSpace(settings.FromAddress) ||
            !Uri.TryCreate(settings.PublicBaseUrl, UriKind.Absolute, out var baseUri) ||
            baseUri.Scheme is not ("http" or "https"))
        {
            throw new InvalidOperationException("SMTP 主机、发件地址或审批中心公开地址未正确配置。");
        }

        if (string.IsNullOrWhiteSpace(message.RecipientAddress))
        {
            throw new InvalidOperationException("邮件通知缺少收件地址。");
        }

        // 仅拼接本站相对路径，正文不含免登录令牌；收件人打开链接仍由审批中心鉴权。
        var target = new Uri(baseUri, message.TargetPath).AbsoluteUri;
        using var mail = new MailMessage(new MailAddress(settings.FromAddress),
            new MailAddress(message.RecipientAddress))
        {
            Subject = message.Subject,
            Body = message.Body + Environment.NewLine + Environment.NewLine + "打开审批中心：" + target,
            IsBodyHtml = false,
            SubjectEncoding = Encoding.UTF8,
            BodyEncoding = Encoding.UTF8,
        };
        mail.Headers.Add("X-Approval-Notification-Id", message.Id);
        using var smtp = new SmtpClient(settings.Host, settings.Port)
        {
            EnableSsl = settings.EnableSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            UseDefaultCredentials = false,
        };
        if (!string.IsNullOrWhiteSpace(settings.UserName))
        {
            smtp.Credentials = new NetworkCredential(settings.UserName, settings.Password);
        }

        await smtp.SendMailAsync(mail, cancellationToken);
    }
}
