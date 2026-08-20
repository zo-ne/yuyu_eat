using FluentAssertions;
using Yustore.Data;
using Yustore.Enums;
using Yustore.Models.Entities;
using Yustore.Services;
using Yustore.Tests.TestHelpers;

namespace Yustore.Tests.Services
{
    public class AdminServiceTests
    {
        private static AdminService CreateSut(AppDbContext db) => new(db);

        // ════════════════════════════════════════
        // 審核佇列
        // ════════════════════════════════════════

        [Fact]
        public async Task GetPendingApplicationsAsync_Only_Returns_Pending_Users()
        {
            var db = InMemoryDbContextFactory.Create();
            db.Users.AddRange(
                new ApplicationUser { Id = "pending-owner", FullName = "審核中老闆", Role = UserRole.Owner, ApplicationStatus = ApplicationStatus.Pending },
                new ApplicationUser { Id = "approved-owner", FullName = "已核准老闆", Role = UserRole.Owner, ApplicationStatus = ApplicationStatus.Approved },
                new ApplicationUser { Id = "rejected-driver", FullName = "已拒絕外送員", Role = UserRole.Driver, ApplicationStatus = ApplicationStatus.Rejected },
                new ApplicationUser { Id = "customer", FullName = "顧客", Role = UserRole.Customer, ApplicationStatus = ApplicationStatus.Approved });
            await db.SaveChangesAsync();
            var sut = CreateSut(db);

            var result = await sut.GetPendingApplicationsAsync(page: 1);

            result.Items.Should().ContainSingle(u => u.Id == "pending-owner");
        }

        [Fact]
        public async Task ApproveApplicationAsync_Sets_Approved_And_Clears_Rejection_Reason()
        {
            var db = InMemoryDbContextFactory.Create();
            var user = new ApplicationUser
            {
                Id = "owner-1",
                FullName = "老闆",
                Role = UserRole.Owner,
                ApplicationStatus = ApplicationStatus.Pending,
                ApplicationRejectionReason = "之前被拒絕過的舊理由"
            };
            db.Users.Add(user);
            await db.SaveChangesAsync();
            var sut = CreateSut(db);

            var result = await sut.ApproveApplicationAsync("owner-1");

            result.Success.Should().BeTrue();
            user.ApplicationStatus.Should().Be(ApplicationStatus.Approved);
            user.ApplicationRejectionReason.Should().BeNull();
        }

        [Fact]
        public async Task ApproveApplicationAsync_Returns_NotFound_For_Unknown_User()
        {
            var db = InMemoryDbContextFactory.Create();
            var sut = CreateSut(db);

            var result = await sut.ApproveApplicationAsync("no-such-user");

            result.Result.Should().Be(AdminOpResultKind.NotFound);
        }

        [Fact]
        public async Task ApproveApplicationAsync_Rejects_Users_Not_Currently_Pending()
        {
            // 已經核准過的帳號不能再被「核准」一次，避免審核紀錄被搞混。
            var db = InMemoryDbContextFactory.Create();
            db.Users.Add(new ApplicationUser { Id = "owner-1", FullName = "老闆", Role = UserRole.Owner, ApplicationStatus = ApplicationStatus.Approved });
            await db.SaveChangesAsync();
            var sut = CreateSut(db);

            var result = await sut.ApproveApplicationAsync("owner-1");

            result.Result.Should().Be(AdminOpResultKind.ValidationFailed);
        }

        [Fact]
        public async Task RejectApplicationAsync_Sets_Rejected_And_Stores_Reason()
        {
            var db = InMemoryDbContextFactory.Create();
            var user = new ApplicationUser { Id = "driver-1", FullName = "外送員", Role = UserRole.Driver, ApplicationStatus = ApplicationStatus.Pending };
            db.Users.Add(user);
            await db.SaveChangesAsync();
            var sut = CreateSut(db);

            var result = await sut.RejectApplicationAsync("driver-1", "證件照片不清楚");

            result.Success.Should().BeTrue();
            user.ApplicationStatus.Should().Be(ApplicationStatus.Rejected);
            user.ApplicationRejectionReason.Should().Be("證件照片不清楚");
        }

        [Fact]
        public async Task RejectApplicationAsync_Requires_A_Reason()
        {
            var db = InMemoryDbContextFactory.Create();
            db.Users.Add(new ApplicationUser { Id = "driver-1", FullName = "外送員", Role = UserRole.Driver, ApplicationStatus = ApplicationStatus.Pending });
            await db.SaveChangesAsync();
            var sut = CreateSut(db);

            var result = await sut.RejectApplicationAsync("driver-1", "   ");

            result.Result.Should().Be(AdminOpResultKind.ValidationFailed);
        }

        // ════════════════════════════════════════
        // 使用者管理 / 停權
        // ════════════════════════════════════════

        [Fact]
        public async Task GetUsersAsync_Filters_By_Role()
        {
            var db = InMemoryDbContextFactory.Create();
            db.Users.AddRange(
                new ApplicationUser { Id = "owner-1", FullName = "老闆", Role = UserRole.Owner },
                new ApplicationUser { Id = "driver-1", FullName = "外送員", Role = UserRole.Driver });
            await db.SaveChangesAsync();
            var sut = CreateSut(db);

            var result = await sut.GetUsersAsync(UserRole.Owner, page: 1);

            result.Items.Should().ContainSingle(u => u.Id == "owner-1");
        }

        [Fact]
        public async Task SetActiveAsync_Suspends_And_Reinstates_A_User()
        {
            var db = InMemoryDbContextFactory.Create();
            var user = new ApplicationUser { Id = "customer-1", FullName = "顧客", Role = UserRole.Customer, IsActive = true };
            db.Users.Add(user);
            await db.SaveChangesAsync();
            var sut = CreateSut(db);

            var suspend = await sut.SetActiveAsync("customer-1", false);
            suspend.Success.Should().BeTrue();
            user.IsActive.Should().BeFalse();

            var reinstate = await sut.SetActiveAsync("customer-1", true);
            reinstate.Success.Should().BeTrue();
            user.IsActive.Should().BeTrue();
        }

        [Fact]
        public async Task SetActiveAsync_Refuses_To_Suspend_An_Admin()
        {
            // Admin 沒有其他審核路徑可以恢復，誤停權會直接鎖死治理後台本身。
            var db = InMemoryDbContextFactory.Create();
            var admin = new ApplicationUser { Id = "admin-1", FullName = "管理員", Role = UserRole.Admin, IsActive = true };
            db.Users.Add(admin);
            await db.SaveChangesAsync();
            var sut = CreateSut(db);

            var result = await sut.SetActiveAsync("admin-1", false);

            result.Result.Should().Be(AdminOpResultKind.ValidationFailed);
            admin.IsActive.Should().BeTrue();
        }

        // ════════════════════════════════════════
        // 全平台訂單總覽
        // ════════════════════════════════════════

        [Fact]
        public async Task GetOrdersAsync_Filters_By_Status_And_Restaurant()
        {
            var db = InMemoryDbContextFactory.Create();
            // 注意：ApplicationUser.Restaurant 是單一參考（不是集合），EF Core 因此把
            // Owner↔Restaurant 當成 1:1 關聯，兩家店不能共用同一個 OwnerId（見 RestaurantServiceTests）。
            var owner1 = new ApplicationUser { Id = "owner-1", FullName = "店長A", Role = UserRole.Owner };
            var owner2 = new ApplicationUser { Id = "owner-2", FullName = "店長B", Role = UserRole.Owner };
            var customer = new ApplicationUser { Id = "customer-1", FullName = "顧客", Role = UserRole.Customer };
            var restaurant1 = new Restaurant { Id = 1, Name = "店A", OwnerId = owner1.Id };
            var restaurant2 = new Restaurant { Id = 2, Name = "店B", OwnerId = owner2.Id };
            db.Users.AddRange(owner1, owner2, customer);
            db.Restaurants.AddRange(restaurant1, restaurant2);
            db.Orders.AddRange(
                new Order { Id = 1, OrderNumber = "A", RestaurantId = 1, CustomerId = customer.Id, Status = OrderStatus.Completed },
                new Order { Id = 2, OrderNumber = "B", RestaurantId = 1, CustomerId = customer.Id, Status = OrderStatus.Paid },
                new Order { Id = 3, OrderNumber = "C", RestaurantId = 2, CustomerId = customer.Id, Status = OrderStatus.Completed });
            await db.SaveChangesAsync();
            var sut = CreateSut(db);

            var result = await sut.GetOrdersAsync(
                status: OrderStatus.Completed, from: null, to: null, restaurantId: 1, page: 1);

            result.Items.Should().ContainSingle(o => o.OrderNumber == "A");
        }

        [Fact]
        public async Task GetOrdersAsync_Filters_By_Date_Range_Inclusive_Of_The_End_Date()
        {
            var db = InMemoryDbContextFactory.Create();
            var owner = new ApplicationUser { Id = "owner-1", FullName = "店長", Role = UserRole.Owner };
            var customer = new ApplicationUser { Id = "customer-1", FullName = "顧客", Role = UserRole.Customer };
            var restaurant = new Restaurant { Id = 1, Name = "店A", OwnerId = owner.Id };
            db.Users.AddRange(owner, customer);
            db.Restaurants.Add(restaurant);
            var targetDay = new DateTime(2026, 3, 15);
            db.Orders.AddRange(
                new Order { Id = 1, OrderNumber = "早一天", RestaurantId = 1, CustomerId = customer.Id, CreatedAt = targetDay.AddDays(-1) },
                new Order { Id = 2, OrderNumber = "當天稍晚", RestaurantId = 1, CustomerId = customer.Id, CreatedAt = targetDay.AddHours(23) },
                new Order { Id = 3, OrderNumber = "晚一天", RestaurantId = 1, CustomerId = customer.Id, CreatedAt = targetDay.AddDays(1) });
            await db.SaveChangesAsync();
            var sut = CreateSut(db);

            var result = await sut.GetOrdersAsync(
                status: null, from: targetDay, to: targetDay, restaurantId: null, page: 1);

            result.Items.Should().ContainSingle(o => o.OrderNumber == "當天稍晚");
        }
    }
}
