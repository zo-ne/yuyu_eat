namespace Yustore.Services
{
    // Interface（介面）= 規定這個服務「必須有哪些功能」
    // 就像合約：「凡是 EmailService 都必須有 SendEmailAsync 這個方法」
    // 好處：未來想換寄信方式，只要換實作，其他程式碼不用改
    public interface IEmailService
    {
        Task SendEmailAsync(string toEmail, string subject, string body);
    }
}