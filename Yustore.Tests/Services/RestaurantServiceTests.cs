using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Yustore.Data;
using Yustore.Enums;
using Yustore.Models.Entities;
using Yustore.Services;
using Yustore.Tests.TestHelpers;
using Yustore.ViewModels;

namespace Yustore.Tests.Services
{
    public class RestaurantServiceTests
    {
        // 沒有 IImageService 依賴的測試不需要圖片，用 null 也能構造 Service（不會被呼叫到）
        private static RestaurantService CreateSut(AppDbContext db) => new(db, new FakeImageService());

        private sealed class FakeImageService : IImageService
        {
            public Task<string> SaveImageAsync(IFormFile file, string folder) =>
                Task.FromResult("/uploads/fake.webp");

            public void DeleteImage(string? imageUrl) { }
        }

        private static async Task<(AppDbContext Db, ApplicationUser Owner)> SeedOwnerAsync(
            string ownerId = "owner-1", AppDbContext? db = null)
        {
            db ??= InMemoryDbContextFactory.Create();
            var owner = new ApplicationUser { Id = ownerId, FullName = "店長", Role = UserRole.Owner };
            db.Users.Add(owner);
            await db.SaveChangesAsync();
            return (db, owner);
        }

        [Fact]
        public async Task SearchOpenRestaurantsAsync_Excludes_Closed_Restaurants()
        {
            // 注意：ApplicationUser.Restaurant 是單一參考（不是集合），EF Core 因此把
            // Owner↔Restaurant 當成 1:1 關聯（跟「一位老闆一家店」的業務規則一致）。
            // 兩家店不能共用同一個 OwnerId，這裡特別用兩個不同的老闆。
            var (db, owner1) = await SeedOwnerAsync("owner-1");
            var (_, owner2) = await SeedOwnerAsync("owner-2", db);
            db.Restaurants.AddRange(
                new Restaurant { Id = 1, Name = "開著的店", OwnerId = owner1.Id, IsOpen = true },
                new Restaurant { Id = 2, Name = "關著的店", OwnerId = owner2.Id, IsOpen = false });
            await db.SaveChangesAsync();
            var sut = CreateSut(db);

            var result = await sut.SearchOpenRestaurantsAsync(search: null, page: 1);

            result.Items.Should().ContainSingle(r => r.Name == "開著的店");
        }

        [Fact]
        public async Task SearchOpenRestaurantsAsync_Filters_By_Search_Term()
        {
            var (db, owner1) = await SeedOwnerAsync("owner-1");
            var (_, owner2) = await SeedOwnerAsync("owner-2", db);
            db.Restaurants.AddRange(
                new Restaurant { Id = 1, Name = "阿裕便當", OwnerId = owner1.Id, IsOpen = true },
                new Restaurant { Id = 2, Name = "麵店", OwnerId = owner2.Id, IsOpen = true });
            await db.SaveChangesAsync();
            var sut = CreateSut(db);

            var result = await sut.SearchOpenRestaurantsAsync(search: "便當", page: 1);

            result.Items.Should().ContainSingle(r => r.Name == "阿裕便當");
        }

        [Fact]
        public async Task GetDetailAsync_Returns_Null_For_Closed_Restaurant()
        {
            var (db, owner) = await SeedOwnerAsync();
            db.Restaurants.Add(new Restaurant { Id = 1, Name = "關著的店", OwnerId = owner.Id, IsOpen = false });
            await db.SaveChangesAsync();
            var sut = CreateSut(db);

            (await sut.GetDetailAsync(1)).Should().BeNull();
        }

        [Fact]
        public async Task GetDetailAsync_Computes_Average_Rating_From_Owner_Reviews()
        {
            var (db, owner) = await SeedOwnerAsync();
            db.Restaurants.Add(new Restaurant { Id = 1, Name = "測試店", OwnerId = owner.Id, IsOpen = true });
            var customer = new ApplicationUser { Id = "customer-1", FullName = "小明", Role = UserRole.Customer };
            db.Users.Add(customer);
            db.Orders.Add(new Order { Id = 1, CustomerId = customer.Id, RestaurantId = 1, OrderNumber = "ORD-1" });
            db.Reviews.AddRange(
                new Review { OrderId = 1, ReviewerId = customer.Id, TargetUserId = owner.Id, TargetType = ReviewTargetType.Owner, Stars = 5 },
                new Review { OrderId = 1, ReviewerId = customer.Id, TargetUserId = owner.Id, TargetType = ReviewTargetType.Owner, Stars = 3 });
            await db.SaveChangesAsync();
            var sut = CreateSut(db);

            var detail = await sut.GetDetailAsync(1);

            detail.Should().NotBeNull();
            detail!.ReviewCount.Should().Be(2);
            detail.AverageRating.Should().Be(4.0);
        }

        [Fact]
        public async Task CreateAsync_Rejects_A_Second_Restaurant_For_The_Same_Owner()
        {
            var (db, owner) = await SeedOwnerAsync();
            db.Restaurants.Add(new Restaurant { Id = 1, Name = "第一家店", OwnerId = owner.Id });
            await db.SaveChangesAsync();
            var sut = CreateSut(db);

            var result = await sut.CreateAsync(owner.Id, "第二家店", null, null, null, null);

            result.Result.Should().Be(RestaurantOpResult.AlreadyExists);
        }

        [Fact]
        public async Task CreateAsync_Succeeds_For_A_New_Owner()
        {
            var (db, owner) = await SeedOwnerAsync();
            var sut = CreateSut(db);

            var result = await sut.CreateAsync(owner.Id, "新開的店", "描述", "地址", "0912345678", null);

            result.Success.Should().BeTrue();
            result.Restaurant!.Name.Should().Be("新開的店");
        }

        [Fact]
        public async Task DeleteMenuItemAsync_Soft_Deletes_Instead_Of_Removing_The_Row()
        {
            // V-02 修復的迴歸測試
            var (db, owner) = await SeedOwnerAsync();
            var restaurant = new Restaurant { Id = 1, Name = "測試店", OwnerId = owner.Id };
            var menuItem = new MenuItem { Id = 1, Name = "便當", Price = 80, RestaurantId = 1 };
            db.Restaurants.Add(restaurant);
            db.MenuItems.Add(menuItem);
            await db.SaveChangesAsync();
            var sut = CreateSut(db);

            var result = await sut.DeleteMenuItemAsync(owner.Id, menuItem.Id);

            result.Success.Should().BeTrue();
            // 軟刪除之後，透過一般查詢方法（會套用全域篩選器）應該再也找不到它
            (await sut.GetMenuItemAsync(owner.Id, menuItem.Id)).Should().BeNull();
        }

        [Fact]
        public async Task UpdateMenuItemAsync_Rejects_MenuItem_Belonging_To_A_Different_Owner()
        {
            var (db, owner) = await SeedOwnerAsync();
            var otherOwner = new ApplicationUser { Id = "owner-2", FullName = "另一個店長", Role = UserRole.Owner };
            db.Users.Add(otherOwner);
            db.Restaurants.Add(new Restaurant { Id = 1, Name = "別人的店", OwnerId = otherOwner.Id });
            db.MenuItems.Add(new MenuItem { Id = 1, Name = "別人的餐點", Price = 80, RestaurantId = 1 });
            await db.SaveChangesAsync();
            var sut = CreateSut(db);

            var result = await sut.UpdateMenuItemAsync(owner.Id, new MenuItemViewModel { Id = 1, Name = "改名", Price = 100 });

            result.Result.Should().Be(RestaurantOpResult.NotFound);
        }
    }
}
