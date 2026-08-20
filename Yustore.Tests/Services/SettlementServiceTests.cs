using FluentAssertions;
using Yustore.Enums;
using Yustore.Models.Entities;
using Yustore.Services;
using Yustore.Tests.TestHelpers;

namespace Yustore.Tests.Services
{
    public class SettlementServiceTests
    {
        [Fact]
        public async Task CreateForDeliveryAsync_Creates_An_Unsettled_Record_For_The_Restaurant_Owner()
        {
            var db = InMemoryDbContextFactory.Create();
            var owner = new ApplicationUser { Id = "owner-1", FullName = "店長", Role = UserRole.Owner };
            var driver = new ApplicationUser { Id = "driver-1", FullName = "外送員", Role = UserRole.Driver };
            var restaurant = new Restaurant { Id = 1, Name = "測試店", OwnerId = owner.Id };
            db.Users.AddRange(owner, driver);
            db.Restaurants.Add(restaurant);
            await db.SaveChangesAsync();
            var sut = new SettlementService(db);

            var settlement = await sut.CreateForDeliveryAsync(
                orderId: 42, restaurantId: restaurant.Id, foodTotal: 255m, driverId: driver.Id);

            settlement.OrderId.Should().Be(42);
            settlement.DriverId.Should().Be(driver.Id);
            settlement.OwnerId.Should().Be(owner.Id); // 從 Restaurant 推導出來的，不是呼叫端直接傳的
            settlement.FoodAmount.Should().Be(255m);
            settlement.Status.Should().Be(SettlementStatus.Unsettled);
            settlement.Year.Should().Be(DateTime.Now.Year);
            settlement.Month.Should().Be(DateTime.Now.Month);
        }

        [Fact]
        public async Task CreateForDeliveryAsync_Throws_For_An_Unknown_Restaurant()
        {
            var db = InMemoryDbContextFactory.Create();
            var sut = new SettlementService(db);

            var act = () => sut.CreateForDeliveryAsync(orderId: 1, restaurantId: 999, foodTotal: 100m, driverId: "driver-1");

            await act.Should().ThrowAsync<InvalidOperationException>();
        }
    }
}
