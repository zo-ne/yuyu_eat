using Yustore.Models.Entities;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Yustore.Data
{
    public class AppDbContext : IdentityDbContext<ApplicationUser>
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<Restaurant> Restaurants { get; set; }
        public DbSet<MenuItem> MenuItems { get; set; }
        public DbSet<Order> Orders { get; set; }
        public DbSet<OrderItem> OrderItems { get; set; }
        public DbSet<Delivery> Deliveries { get; set; }
        public DbSet<OrderTransaction> OrderTransactions { get; set; }
        public DbSet<SettlementBatch> SettlementBatches { get; set; }
        public DbSet<Review> Reviews { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            // ── Review ──────────────────────────────────────
            // V-01 修復（最後一道防線）：Controller 層已經驗證過評分對象合法性，
            // 這裡再加一個 DB 層的 unique 約束，就算未來哪個新的呼叫路徑忘記檢查，
            // 同一個人也不可能對同一筆訂單的同一個對象重複建立第二筆評分。
            builder.Entity<Review>()
                .HasIndex(r => new { r.OrderId, r.ReviewerId, r.TargetUserId })
                .IsUnique();

            builder.Entity<Review>()
                .HasOne(r => r.Reviewer)
                .WithMany(u => u.ReviewsGiven)
                .HasForeignKey(r => r.ReviewerId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<Review>()
                .HasOne(r => r.TargetUser)
                .WithMany(u => u.ReviewsReceived)
                .HasForeignKey(r => r.TargetUserId)
                .OnDelete(DeleteBehavior.Restrict);

            // ── OrderTransaction / SettlementBatch（M4 修復：Settlement 拆分）──
            builder.Entity<OrderTransaction>()
                .HasOne(t => t.Order)
                .WithOne(o => o.Transaction)
                .HasForeignKey<OrderTransaction>(t => t.OrderId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<OrderTransaction>()
                .HasOne(t => t.Owner)
                .WithMany()
                .HasForeignKey(t => t.OwnerId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<OrderTransaction>()
                .HasOne(t => t.Driver)
                .WithMany()
                .HasForeignKey(t => t.DriverId)
                .OnDelete(DeleteBehavior.Restrict);

            // 一筆交易的店家分潤跟外送員分潤是兩個獨立的批次歸屬，各自設一條 Restrict 的關聯
            builder.Entity<OrderTransaction>()
                .HasOne(t => t.OwnerSettlementBatch)
                .WithMany(b => b.OwnerTransactions)
                .HasForeignKey(t => t.OwnerSettlementBatchId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<OrderTransaction>()
                .HasOne(t => t.DriverSettlementBatch)
                .WithMany(b => b.DriverTransactions)
                .HasForeignKey(t => t.DriverSettlementBatchId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<SettlementBatch>()
                .HasOne(b => b.Payee)
                .WithMany()
                .HasForeignKey(b => b.PayeeId)
                .OnDelete(DeleteBehavior.Restrict);

            // 同一個收款人、同一個月份最多只能有一個批次，不然「產生本月結算批次」按兩次會重複入帳
            builder.Entity<SettlementBatch>()
                .HasIndex(b => new { b.PayeeId, b.Year, b.Month })
                .IsUnique();

            // ── Order ────────────────────────────────────────
            // V-10 修復：OrderNumber 原本沒有 unique 約束，碰撞會安靜地產生兩張同編號的訂單。
            builder.Entity<Order>()
                .HasIndex(o => o.OrderNumber)
                .IsUnique();

            builder.Entity<Order>()
                .HasOne(o => o.Customer)
                .WithMany(u => u.CustomerOrders)
                .HasForeignKey(o => o.CustomerId)
                .OnDelete(DeleteBehavior.Restrict);

            // Order 跟 Restaurant 的關聯也要 Restrict
            builder.Entity<Order>()
                .HasOne(o => o.Restaurant)
                .WithMany(r => r.Orders)
                .HasForeignKey(o => o.RestaurantId)
                .OnDelete(DeleteBehavior.Restrict);

            // ── OrderItem ────────────────────────────────────
            // V-02 修復：這裡原本沒有設定，吃到 EF 預設的 Cascade。
            // 老闆刪除餐點時會連鎖刪除所有曾經點過這道菜的 OrderItem，
            // 導致歷史訂單明細消失、訂單金額對不起來。改成 Restrict：
            // 有歷史訂單引用的 MenuItem 無法被硬刪除（OwnerController 改用軟刪除，見 MenuItem.IsDeleted）。
            builder.Entity<OrderItem>()
                .HasOne(oi => oi.MenuItem)
                .WithMany(m => m.OrderItems)
                .HasForeignKey(oi => oi.MenuItemId)
                .OnDelete(DeleteBehavior.Restrict);

            // ── MenuItem 軟刪除 ─────────────────────────────
            // 全域查詢篩選器：一般查詢（包含 Include 帶出的導覽屬性）都自動排除已刪除的餐點。
            // 注意：因此顯示歷史訂單明細時不能依賴 OrderItem.MenuItem 導覽屬性
            //（餐點被軟刪除後這個導覽屬性會是 null），要改讀 OrderItem.MenuItemName 快照。
            builder.Entity<MenuItem>()
                .HasQueryFilter(m => !m.IsDeleted);

            // ── Delivery ─────────────────────────────────────
            // 這就是剛才出錯的地方！
            // Delivery 同時關聯 Order 和 User，會造成多條刪除路徑
            // 所以兩個都要設成 Restrict
            builder.Entity<Delivery>()
                .HasOne(d => d.Driver)
                .WithMany()
                .HasForeignKey(d => d.DriverId)
                .OnDelete(DeleteBehavior.Restrict); // ← 不自動連鎖刪除

            builder.Entity<Delivery>()
                .HasOne(d => d.Order)
                .WithOne(o => o.Delivery)
                .HasForeignKey<Delivery>(d => d.OrderId)
                .OnDelete(DeleteBehavior.Restrict); // ← 不自動連鎖刪除

            // ── decimal 精度 ─────────────────────────────────
            builder.Entity<MenuItem>()
                .Property(m => m.Price)
                .HasColumnType("decimal(10,2)");

            builder.Entity<Order>()
                .Property(o => o.FoodTotal)
                .HasColumnType("decimal(10,2)");
            builder.Entity<Order>()
                .Property(o => o.DeliveryFee)
                .HasColumnType("decimal(10,2)");
            builder.Entity<Order>()
                .Property(o => o.GrandTotal)
                .HasColumnType("decimal(10,2)");

            builder.Entity<OrderItem>()
                .Property(i => i.UnitPrice)
                .HasColumnType("decimal(10,2)");
            builder.Entity<OrderItem>()
                .Property(i => i.Subtotal)
                .HasColumnType("decimal(10,2)");

            builder.Entity<OrderTransaction>()
                .Property(t => t.GrossAmount)
                .HasColumnType("decimal(10,2)");
            builder.Entity<OrderTransaction>()
                .Property(t => t.PlatformFee)
                .HasColumnType("decimal(10,2)");
            builder.Entity<OrderTransaction>()
                .Property(t => t.RestaurantPayout)
                .HasColumnType("decimal(10,2)");
            builder.Entity<OrderTransaction>()
                .Property(t => t.DriverPayout)
                .HasColumnType("decimal(10,2)");
            builder.Entity<SettlementBatch>()
                .Property(b => b.TotalAmount)
                .HasColumnType("decimal(10,2)");
        }
    }
} 