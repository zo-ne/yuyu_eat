namespace Yustore.Options
{
    // M3 修復：原本 EmailService 到處用 _config["EmailSettings:SmtpHost"] 這種字串索引，
    // 打錯字或改了 appsettings.json 的鍵名都要等到執行時才會發現。改用 Options Pattern，
    // 設定值變成強型別，缺漏在啟動時就能發現（IOptions<T> 走 DI，也方便測試時注入假設定）。
    public class EmailSettings
    {
        public const string SectionName = "EmailSettings";

        public string SmtpHost { get; set; } = string.Empty;
        public int SmtpPort { get; set; }
        public string SenderEmail { get; set; } = string.Empty;
        public string SenderName { get; set; } = string.Empty;
        public string AppPassword { get; set; } = string.Empty;
    }
}
