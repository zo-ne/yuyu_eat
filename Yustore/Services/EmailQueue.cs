using System.Threading.Channels;

namespace Yustore.Services
{
    public record EmailMessage(string ToEmail, string Subject, string Body);

    // M3 修復（P-05）：Controller 端只把信丟進這個佇列，不等 SMTP 連線/寄送完成，
    // HTTP 請求立刻能回應。真正寄信的工作交給 EmailBackgroundService 在背景慢慢處理，
    // 一封信寄失敗也不會拖累其他信、更不會擋住訂單狀態的存檔。
    public interface IEmailQueue
    {
        ValueTask EnqueueAsync(EmailMessage message, CancellationToken cancellationToken = default);
        IAsyncEnumerable<EmailMessage> DequeueAllAsync(CancellationToken cancellationToken);
    }

    public class EmailQueue : IEmailQueue
    {
        // Unbounded：這個專案的寄信量很小（驗證信 + 通知外送師），沒有需要限制佇列長度的理由。
        // 真的量大到需要背壓控制時，才需要換成 BoundedChannel。
        private readonly Channel<EmailMessage> _channel = Channel.CreateUnbounded<EmailMessage>();

        public ValueTask EnqueueAsync(EmailMessage message, CancellationToken cancellationToken = default) =>
            _channel.Writer.WriteAsync(message, cancellationToken);

        public IAsyncEnumerable<EmailMessage> DequeueAllAsync(CancellationToken cancellationToken) =>
            _channel.Reader.ReadAllAsync(cancellationToken);
    }
}
