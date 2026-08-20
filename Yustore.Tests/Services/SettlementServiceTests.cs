using FluentAssertions;
using Yustore.Enums;
using Yustore.Models.Entities;
using Yustore.Services;
using Yustore.Tests.TestHelpers;

namespace Yustore.Tests.Services
{
    public class SettlementServiceTests
    {
        private static async Task<(Yustore.Data.AppDbContext Db, ApplicationUser Owner, ApplicationUser Driver)> SeedAsync()
        {
            var db = InMemoryDbContextFactory.Create();
            var owner = new ApplicationUser { Id = "owner-1", FullName = "店長", Role = UserRole.Owner };
            var driver = new ApplicationUser { Id = "driver-1", FullName = "外送員", Role = UserRole.Driver };
            var restaurant = new Restaurant { Id = 1, Name = "測試店", OwnerId = owner.Id };
            db.Users.AddRange(owner, driver);
            db.Restaurants.Add(restaurant);
            await db.SaveChangesAsync();
            return (db, owner, driver);
        }

        [Fact]
        public async Task CreateForDeliveryAsync_Splits_FoodTotal_By_The_15_Percent_Platform_Commission()
        {
            // 商業模式的核心迴歸測試：餐費 $300 → 平台抽 15% = $45，店家收 $255；
            // 外送費 $30 全額歸外送員，平台不抽外送費。
            var (db, owner, driver) = await SeedAsync();
            var sut = new SettlementService(db);

            var transaction = await sut.CreateForDeliveryAsync(
                orderId: 42, restaurantId: 1, foodTotal: 300m, deliveryFee: 30m, driverId: driver.Id);

            transaction.OwnerId.Should().Be(owner.Id); // 從 Restaurant 推導出來的，不是呼叫端直接傳的
            transaction.DriverId.Should().Be(driver.Id);
            transaction.GrossAmount.Should().Be(330m);
            transaction.PlatformFee.Should().Be(45m);
            transaction.RestaurantPayout.Should().Be(255m);
            transaction.DriverPayout.Should().Be(30m);
        }

        [Fact]
        public async Task CreateForDeliveryAsync_Throws_For_An_Unknown_Restaurant()
        {
            var db = InMemoryDbContextFactory.Create();
            var sut = new SettlementService(db);

            var act = () => sut.CreateForDeliveryAsync(
                orderId: 1, restaurantId: 999, foodTotal: 100m, deliveryFee: 30m, driverId: "driver-1");

            await act.Should().ThrowAsync<InvalidOperationException>();
        }

        [Fact]
        public async Task GenerateMonthlyBatchesAsync_Aggregates_Transactions_By_Payee()
        {
            var (db, owner, driver) = await SeedAsync();
            var sut = new SettlementService(db);
            await sut.CreateForDeliveryAsync(1, 1, 300m, 30m, driver.Id); // 店家 255 / 外送員 30
            await sut.CreateForDeliveryAsync(2, 1, 100m, 30m, driver.Id); // 店家 85 / 外送員 30

            var now = DateTime.Now;
            var touched = await sut.GenerateMonthlyBatchesAsync(now.Year, now.Month);

            touched.Should().Be(2); // 一個店家批次 + 一個外送員批次

            var ownerBatches = await sut.GetBatchesAsync(page: 1, payeeId: owner.Id);
            ownerBatches.Items.Should().ContainSingle(b => b.TotalAmount == 340m); // 255+85

            var driverBatches = await sut.GetBatchesAsync(page: 1, payeeId: driver.Id);
            driverBatches.Items.Should().ContainSingle(b => b.TotalAmount == 60m); // 30+30
        }

        [Fact]
        public async Task GenerateMonthlyBatchesAsync_Is_Idempotent_And_Tops_Up_Existing_Batch()
        {
            // 迴歸測試：同一個月執行兩次不能因為 unique 索引而炸掉，
            // 第二次執行時新出現的交易要「併入」既有批次，而不是重複建一筆。
            var (db, owner, driver) = await SeedAsync();
            var sut = new SettlementService(db);
            var now = DateTime.Now;

            await sut.CreateForDeliveryAsync(1, 1, 300m, 30m, driver.Id);
            await sut.GenerateMonthlyBatchesAsync(now.Year, now.Month);

            await sut.CreateForDeliveryAsync(2, 1, 100m, 30m, driver.Id); // 月底才完成的第二筆訂單
            await sut.GenerateMonthlyBatchesAsync(now.Year, now.Month);

            var ownerBatches = await sut.GetBatchesAsync(page: 1, payeeId: owner.Id);
            ownerBatches.Items.Should().ContainSingle(); // 還是只有一筆批次
            ownerBatches.Items.Single().TotalAmount.Should().Be(340m); // 但金額已經把第二筆加進去了
        }

        [Fact]
        public async Task GenerateMonthlyBatchesAsync_Returns_Zero_When_Nothing_To_Batch()
        {
            var db = InMemoryDbContextFactory.Create();
            var sut = new SettlementService(db);

            var touched = await sut.GenerateMonthlyBatchesAsync(2026, 1);

            touched.Should().Be(0);
        }
    }
}
