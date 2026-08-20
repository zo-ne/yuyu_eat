namespace Yustore.Services
{
    // 背景服務：從 IEmailQueue 一封一封拿出來，用 IEmailService（實際的 SMTP 寄信邏輯）寄出去。
    // 這裡是 Singleton 生命週期，但 IEmailService 是 Scoped，所以每處理一封信都要開一個新的
    // DI scope（標準寫法：BackgroundService 消費 Scoped 服務一定要透過 IServiceScopeFactory）。
    public class EmailBackgroundService : BackgroundService
    {
        private readonly IEmailQueue _queue;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<EmailBackgroundService> _logger;

        public EmailBackgroundService(
            IEmailQueue queue,
            IServiceScopeFactory scopeFactory,
            ILogger<EmailBackgroundService> logger)
        {
            _queue = queue;
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await foreach (var message in _queue.DequeueAllAsync(stoppingToken))
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();
                    await emailService.SendEmailAsync(message.ToEmail, message.Subject, message.Body);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // 一封信寄失敗（例如 SMTP 連線逾時）不該讓整個背景服務停掉，
                    // 記錄下來繼續處理佇列裡的下一封。
                    _logger.LogError(ex, "背景寄信失敗：{ToEmail} / {Subject}", message.ToEmail, message.Subject);
                }
            }
        }
    }
}
