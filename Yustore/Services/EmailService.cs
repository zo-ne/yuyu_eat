using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using Yustore.Options;

namespace Yustore.Services
{
    // EmailService 實作 IEmailService 介面
    // 用 MailKit 套件透過 Gmail SMTP 寄信
    public class EmailService : IEmailService
    {
        private readonly EmailSettings _settings;

        // M3 修復：改用 Options Pattern，設定值是強型別的 EmailSettings，
        // 不用再到處用字串索引 _config["EmailSettings:SmtpHost"] 這種寫法。
        public EmailService(IOptions<EmailSettings> settings)
        {
            _settings = settings.Value;
        }

        public async Task SendEmailAsync(string toEmail, string subject, string body)
        {
            if (string.IsNullOrEmpty(_settings.SmtpHost))
                throw new InvalidOperationException("缺少設定 EmailSettings:SmtpHost");
            if (_settings.SmtpPort == 0)
                throw new InvalidOperationException("缺少設定 EmailSettings:SmtpPort");
            if (string.IsNullOrEmpty(_settings.SenderEmail))
                throw new InvalidOperationException("缺少設定 EmailSettings:SenderEmail");
            if (string.IsNullOrEmpty(_settings.AppPassword))
                throw new InvalidOperationException(
                    "缺少設定 EmailSettings:AppPassword，本機開發請用 dotnet user-secrets 設定。");

            // 建立郵件內容
            var email = new MimeMessage();
            email.From.Add(new MailboxAddress(_settings.SenderName, _settings.SenderEmail));
            email.To.Add(MailboxAddress.Parse(toEmail));
            email.Subject = subject;

            // BodyBuilder 用來建立郵件內文
            var builder = new BodyBuilder();
            builder.HtmlBody = body; // 支援 HTML 格式的信件內容
            email.Body = builder.ToMessageBody();

            // 建立 SMTP 連線並寄信
            using var smtp = new SmtpClient();
            // StartTls = 加密連線，保護帳號密碼安全
            await smtp.ConnectAsync(_settings.SmtpHost, _settings.SmtpPort, SecureSocketOptions.StartTls);
            await smtp.AuthenticateAsync(_settings.SenderEmail, _settings.AppPassword);
            await smtp.SendAsync(email);
            await smtp.DisconnectAsync(true);
        }
    }
}
