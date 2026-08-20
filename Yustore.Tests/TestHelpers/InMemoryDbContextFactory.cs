using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Yustore.Data;

namespace Yustore.Tests.TestHelpers
{
    // OrderService 直接依賴 AppDbContext（EF Core），不是脫離資料庫的純邏輯，
    // 用 EF Core InMemory provider 而不是真的 SQL Server 來測——不用 Docker/Testcontainers，
    // CI 跑起來快，也符合「先做單元測試，不做整合測試」的範圍決定。
    internal static class InMemoryDbContextFactory
    {
        public static AppDbContext Create()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                // InMemory provider 不支援真正的交易，OrderService.CheckoutAsync 會呼叫
                // Database.BeginTransactionAsync()，這裡讓它安靜地當作 no-op，而不是被當成警告噴例外。
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;

            return new AppDbContext(options);
        }
    }
}
