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
        public DbSet<Settlement> Settlements { get; set; }
        public DbSet<Review> Reviews { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            // ── Review ──────────────────────────────────────
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

            // ── Settlement ──────────────────────────────────
            builder.Entity<Settlement>()
                .HasOne(s => s.Driver)
                .WithMany()
                .HasForeignKey(s => s.DriverId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<Settlement>()
                .HasOne(s => s.Owner)
                .WithMany()
                .HasForeignKey(s => s.OwnerId)
                .OnDelete(DeleteBehavior.Restrict);

            // Settlement 跟 Order 的關聯也要 Restrict
            builder.Entity<Settlement>()
                .HasOne(s => s.Order)
                .WithOne(o => o.Settlement)
                .HasForeignKey<Settlement>(s => s.OrderId)
                .OnDelete(DeleteBehavior.Restrict);

            // ── Order ────────────────────────────────────────
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

            builder.Entity<Settlement>()
                .Property(s => s.FoodAmount)
                .HasColumnType("decimal(10,2)");
        }
    }
} 