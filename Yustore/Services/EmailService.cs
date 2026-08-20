using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace Yustore.Services
{
    // EmailService 實作 IEmailService 介面
    // 用 MailKit 套件透過 Gmail SMTP 寄信
    public class EmailService : IEmailService
    {
        private readonly IConfiguration _config;

        // 建構子注入：程式啟動時自動把設定檔（appsettings.json）傳進來
        // IConfiguration 可以讀取 appsettings.json 的內容
        public EmailService(IConfiguration config)
        {
            _config = config;
        }

        public async Task SendEmailAsync(string toEmail, string subject, string body)
        {
            // 從 appsettings.json / User Secrets 讀取 Email 設定。
            // 這幾個值都是寄信功能運作的必要條件，缺一個就直接講清楚是哪個設定沒填，
            // 好過讓 MailKit 在更深的地方丟出一個看不出原因的 NullReferenceException。
            var host = _config["EmailSettings:SmtpHost"]
                ?? throw new InvalidOperationException("缺少設定 EmailSettings:SmtpHost");
            var port = int.Parse(_config["EmailSettings:SmtpPort"]
                ?? throw new InvalidOperationException("缺少設定 EmailSettings:SmtpPort"));
            var senderEmail = _config["EmailSettings:SenderEmail"]
                ?? throw new InvalidOperationException("缺少設定 EmailSettings:SenderEmail");
            var senderName = _config["EmailSettings:SenderName"];
            var appPassword = _config["EmailSettings:AppPassword"]
                ?? throw new InvalidOperationException(
                    "缺少設定 EmailSettings:AppPassword，本機開發請用 dotnet user-secrets 設定。");

            // 建立郵件內容
            var email = new MimeMessage();
            email.From.Add(new MailboxAddress(senderName, senderEmail));
            email.To.Add(MailboxAddress.Parse(toEmail));
            email.Subject = subject;

            // BodyBuilder 用來建立郵件內文
            var builder = new BodyBuilder();
            builder.HtmlBody = body; // 支援 HTML 格式的信件內容
            email.Body = builder.ToMessageBody();

            // 建立 SMTP 連線並寄信
            using var smtp = new SmtpClient();
            // StartTls = 加密連線，保護帳號密碼安全
            await smtp.ConnectAsync(host, port, SecureSocketOptions.StartTls);
            await smtp.AuthenticateAsync(senderEmail, appPassword);
            await smtp.SendAsync(email);
            await smtp.DisconnectAsync(true);
        }
    }
}