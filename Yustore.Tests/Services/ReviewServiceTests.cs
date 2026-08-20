using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Yustore.Data;
using Yustore.Enums;
using Yustore.Models.Entities;
using Yustore.Services;
using Yustore.Tests.TestHelpers;

namespace Yustore.Tests.Services
{
    public class ReviewServiceTests
    {
        private static async Task<(AppDbContext Db, Order Order, ApplicationUser Customer, ApplicationUser Owner, ApplicationUser Driver)>
            SeedDeliveredOrderAsync()
        {
            var db = InMemoryDbContextFactory.Create();

            var customer = new ApplicationUser { Id = "customer-1", FullName = "小明", Role = UserRole.Customer };
            var owner = new ApplicationUser { Id = "owner-1", FullName = "店長", Role = UserRole.Owner };
            var driver = new ApplicationUser { Id = "driver-1", FullName = "外送員", Role = UserRole.Driver };
            var restaurant = new Restaurant { Id = 1, Name = "測試店", OwnerId = owner.Id };
            var order = new Order
            {
                Id = 1,
                OrderNumber = "ORD-TEST-000001",
                Status = OrderStatus.Delivered,
                CustomerId = customer.Id,
                RestaurantId = restaurant.Id,
            };
            var delivery = new Delivery { OrderId = order.Id, DriverId = driver.Id };

            db.Users.AddRange(customer, owner, driver);
            db.Restaurants.Add(restaurant);
            db.Orders.Add(order);
            db.Deliveries.Add(delivery);
            await db.SaveChangesAsync();

            return (db, order, customer, owner, driver);
        }

        [Fact]
        public async Task GetOrderReviewsAsync_Returns_Forbidden_For_An_Unrelated_User()
        {
            var (db, order, _, _, _) = await SeedDeliveredOrderAsync();
            var stranger = new ApplicationUser { Id = "stranger-1", FullName = "路人", Role = UserRole.Customer };
            db.Users.Add(stranger);
            await db.SaveChangesAsync();
            var sut = new ReviewService(db, MakeUserManager(db));

            var result = await sut.GetOrderReviewsAsync(order.Id, stranger);

            result.Result.Should().Be(ReviewOpResult.Forbidden);
        }

        [Fact]
        public async Task GetOrderReviewsAsync_Returns_NotYetCompleted_For_A_PendingPayment_Order()
        {
            var (db, order, customer, _, _) = await SeedDeliveredOrderAsync();
            order.Status = OrderStatus.PendingPayment;
            await db.SaveChangesAsync();
            var sut = new ReviewService(db, MakeUserManager(db));

            var result = await sut.GetOrderReviewsAsync(order.Id, customer);

            result.Result.Should().Be(ReviewOpResult.NotYetCompleted);
        }

        [Fact]
        public async Task GetOrderReviewsAsync_Lists_Pending_Targets_For_The_Customer()
        {
            var (db, order, customer, owner, driver) = await SeedDeliveredOrderAsync();
            var sut = new ReviewService(db, MakeUserManager(db));

            var result = await sut.GetOrderReviewsAsync(order.Id, customer);

            result.Result.Should().Be(ReviewOpResult.Success);
            result.Model!.PendingReviews.Should().HaveCount(2); // 老闆 + 外送師
            result.Model.PendingReviews.Should().OnlyContain(p => !p.AlreadyReviewed);
        }

        [Fact]
        public async Task SubmitReviewAsync_Rejects_A_Target_Not_Related_To_The_Order()
        {
            // V-01 修復的核心迴歸測試：不能對訂單無關的人評分
            var (db, order, customer, _, _) = await SeedDeliveredOrderAsync();
            var stranger = new ApplicationUser { Id = "stranger-1", FullName = "路人", Role = UserRole.Customer };
            db.Users.Add(stranger);
            await db.SaveChangesAsync();
            var sut = new ReviewService(db, MakeUserManager(db));

            var result = await sut.SubmitReviewAsync(order.Id, stranger.Id, customer, stars: 5, comment: null);

            result.Should().Be(ReviewOpResult.Forbidden);
            db.Reviews.Should().BeEmpty();
        }

        [Fact]
        public async Task SubmitReviewAsync_Rejects_Duplicate_Review()
        {
            var (db, order, customer, owner, _) = await SeedDeliveredOrderAsync();
            var sut = new ReviewService(db, MakeUserManager(db));
            await sut.SubmitReviewAsync(order.Id, owner.Id, customer, stars: 5, comment: null);

            var result = await sut.SubmitReviewAsync(order.Id, owner.Id, customer, stars: 3, comment: "again");

            result.Should().Be(ReviewOpResult.AlreadyReviewed);
            db.Reviews.Should().ContainSingle();
        }

        [Fact]
        public async Task SubmitReviewAsync_Keeps_Order_Delivered_Until_All_Required_Pairs_Are_Reviewed()
        {
            // R-4 修復的核心迴歸測試：原本「任一人」評分就轉完成，現在要全部評完才轉
            var (db, order, customer, owner, driver) = await SeedDeliveredOrderAsync();
            var sut = new ReviewService(db, MakeUserManager(db));

            // 顧客評老闆：只完成 6 組配對裡的 1 組，訂單應該還是「已送達」
            await sut.SubmitReviewAsync(order.Id, owner.Id, customer, stars: 5, comment: null);

            (await db.Orders.FindAsync(order.Id))!.Status.Should().Be(OrderStatus.Delivered);
        }

        [Fact]
        public async Task SubmitReviewAsync_Completes_Order_Once_All_Six_Pairs_Are_Reviewed()
        {
            var (db, order, customer, owner, driver) = await SeedDeliveredOrderAsync();
            var sut = new ReviewService(db, MakeUserManager(db));

            await sut.SubmitReviewAsync(order.Id, owner.Id, customer, stars: 5, comment: null);   // 顧客→老闆
            await sut.SubmitReviewAsync(order.Id, driver.Id, customer, stars: 5, comment: null);  // 顧客→外送師
            await sut.SubmitReviewAsync(order.Id, customer.Id, owner, stars: 5, comment: null);   // 老闆→顧客
            await sut.SubmitReviewAsync(order.Id, driver.Id, owner, stars: 5, comment: null);     // 老闆→外送師
            await sut.SubmitReviewAsync(order.Id, customer.Id, driver, stars: 5, comment: null);  // 外送師→顧客

            (await db.Orders.FindAsync(order.Id))!.Status.Should().Be(OrderStatus.Delivered); // 還差最後一組

            var lastResult = await sut.SubmitReviewAsync(order.Id, owner.Id, driver, stars: 5, comment: null); // 外送師→老闆

            lastResult.Should().Be(ReviewOpResult.Success);
            (await db.Orders.FindAsync(order.Id))!.Status.Should().Be(OrderStatus.Completed);
        }

        // ReviewService 建構子需要 UserManager<ApplicationUser>，只用到 FindByIdAsync，
        // 用 ASP.NET Core Identity 官方提供的 InMemory store 建一個真的可用的 UserManager，
        // 不用手刻一個假的（UserManager 本身沒有介面可以 mock，硬 mock 反而更脆弱）。
        private static Microsoft.AspNetCore.Identity.UserManager<ApplicationUser> MakeUserManager(AppDbContext db)
        {
            var store = new Microsoft.AspNetCore.Identity.EntityFrameworkCore.UserStore<ApplicationUser>(db);
            var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection()
                .BuildServiceProvider();

            return new Microsoft.AspNetCore.Identity.UserManager<ApplicationUser>(
                store,
                Microsoft.Extensions.Options.Options.Create(new Microsoft.AspNetCore.Identity.IdentityOptions()),
                new Microsoft.AspNetCore.Identity.PasswordHasher<ApplicationUser>(),
                Array.Empty<Microsoft.AspNetCore.Identity.IUserValidator<ApplicationUser>>(),
                Array.Empty<Microsoft.AspNetCore.Identity.IPasswordValidator<ApplicationUser>>(),
                new Microsoft.AspNetCore.Identity.UpperInvariantLookupNormalizer(),
                new Microsoft.AspNetCore.Identity.IdentityErrorDescriber(),
                services,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<Microsoft.AspNetCore.Identity.UserManager<ApplicationUser>>.Instance);
        }
    }
}
