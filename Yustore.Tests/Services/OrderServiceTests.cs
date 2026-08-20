using FluentAssertions;
using Yustore.Data;
using Yustore.Enums;
using Yustore.Models.Entities;
using Yustore.Services;
using Yustore.Tests.TestHelpers;
using Yustore.ViewModels;

namespace Yustore.Tests.Services
{
    public class OrderServiceTests
    {
        private static async Task<(AppDbContext Db, Restaurant Restaurant, MenuItem MenuItem, ApplicationUser Customer)>
            SeedAsync(decimal menuItemPrice = 80, bool isAvailable = true)
        {
            var db = InMemoryDbContextFactory.Create();

            var owner = new ApplicationUser { Id = "owner-1", FullName = "店長", Role = UserRole.Owner };
            var customer = new ApplicationUser { Id = "customer-1", FullName = "小明", Role = UserRole.Customer };
            var restaurant = new Restaurant { Id = 1, Name = "測試店", OwnerId = owner.Id, IsOpen = true };
            var menuItem = new MenuItem
            {
                Id = 1,
                Name = "便當",
                Price = menuItemPrice,
                IsAvailable = isAvailable,
                RestaurantId = restaurant.Id,
            };

            db.Users.AddRange(owner, customer);
            db.Restaurants.Add(restaurant);
            db.MenuItems.Add(menuItem);
            await db.SaveChangesAsync();

            return (db, restaurant, menuItem, customer);
        }

        // ── CheckoutAsync ──────────────────────────────────────

        [Fact]
        public async Task CheckoutAsync_With_Empty_Cart_Fails()
        {
            var (db, _, _, customer) = await SeedAsync();
            var sut = new OrderService(db);

            var result = await sut.CheckoutAsync(customer.Id, new CartViewModel(), "地址", null);

            result.Success.Should().BeFalse();
            result.ErrorMessage.Should().NotBeNullOrEmpty();
        }

        [Fact]
        public async Task CheckoutAsync_Rejects_MenuItem_That_Is_No_Longer_Available()
        {
            // V-04 修復的核心情境：老闆在顧客結帳前把餐點下架
            var (db, restaurant, menuItem, customer) = await SeedAsync(isAvailable: false);
            var sut = new OrderService(db);
            var cart = new CartViewModel
            {
                RestaurantId = restaurant.Id,
                Items = { new CartItemViewModel { MenuItemId = menuItem.Id, Name = "便當", Price = 80, Quantity = 1 } }
            };

            var result = await sut.CheckoutAsync(customer.Id, cart, "地址", null);

            result.Success.Should().BeFalse();
            result.ErrorMessage.Should().Contain("下架");
        }

        [Fact]
        public async Task CheckoutAsync_Rejects_MenuItem_From_A_Different_Restaurant()
        {
            var (db, _, menuItem, customer) = await SeedAsync();
            var sut = new OrderService(db);
            var cart = new CartViewModel
            {
                RestaurantId = 999, // 跟 menuItem 實際所屬的餐廳不一致
                Items = { new CartItemViewModel { MenuItemId = menuItem.Id, Name = "便當", Price = 80, Quantity = 1 } }
            };

            var result = await sut.CheckoutAsync(customer.Id, cart, "地址", null);

            result.Success.Should().BeFalse();
        }

        [Fact]
        public async Task CheckoutAsync_Uses_Current_Database_Price_Not_Stale_Cart_Price()
        {
            // V-04 修復的核心情境：購物車裡是加入當下的舊價格（80），
            // 老闆結帳前把價格改成 120，最後訂單金額必須用資料庫的新價格算。
            var (db, restaurant, menuItem, customer) = await SeedAsync(menuItemPrice: 120);
            var sut = new OrderService(db);
            var cart = new CartViewModel
            {
                RestaurantId = restaurant.Id,
                Items = { new CartItemViewModel { MenuItemId = menuItem.Id, Name = "便當", Price = 80, Quantity = 2 } } // 舊價格快照
            };

            var result = await sut.CheckoutAsync(customer.Id, cart, "台北市中山路1號", "備註");

            result.Success.Should().BeTrue();
            result.Order!.FoodTotal.Should().Be(240m); // 120 * 2，不是 80 * 2
            result.Order.DeliveryFee.Should().Be(30m);
            result.Order.GrandTotal.Should().Be(270m);
            result.Order.Status.Should().Be(OrderStatus.PendingPayment);
            result.Order.OrderItems.Single().MenuItemName.Should().Be("便當"); // 名稱快照（V-02 修復）
        }

        [Fact]
        public async Task CheckoutAsync_Generates_Sequential_Unique_OrderNumbers_Within_The_Same_Day()
        {
            var (db, restaurant, menuItem, customer) = await SeedAsync();
            var sut = new OrderService(db);
            var cart = new CartViewModel
            {
                RestaurantId = restaurant.Id,
                Items = { new CartItemViewModel { MenuItemId = menuItem.Id, Name = "便當", Price = 80, Quantity = 1 } }
            };

            var first = await sut.CheckoutAsync(customer.Id, cart, "地址", null);
            var second = await sut.CheckoutAsync(customer.Id, cart, "地址", null);

            first.Order!.OrderNumber.Should().NotBe(second.Order!.OrderNumber);
            first.Order.OrderNumber.Should().StartWith($"ORD-{DateTime.Now:yyyyMMdd}-");
        }

        // ── ConfirmPaymentAsync ────────────────────────────────

        [Fact]
        public async Task ConfirmPaymentAsync_For_Unknown_Order_Returns_Null()
        {
            var (db, _, _, customer) = await SeedAsync();
            var sut = new OrderService(db);

            var result = await sut.ConfirmPaymentAsync(orderId: 999, customer.Id);

            result.Should().BeNull();
        }

        [Fact]
        public async Task ConfirmPaymentAsync_Flips_Status_To_Paid()
        {
            var (db, restaurant, _, customer) = await SeedAsync();
            var order = new Order
            {
                OrderNumber = "ORD-TEST-000001",
                Status = OrderStatus.PendingPayment,
                CustomerId = customer.Id,
                RestaurantId = restaurant.Id,
            };
            db.Orders.Add(order);
            await db.SaveChangesAsync();
            var sut = new OrderService(db);

            var result = await sut.ConfirmPaymentAsync(order.Id, customer.Id);

            result.Should().NotBeNull();
            result!.Status.Should().Be(OrderStatus.Paid);
        }

        // ── UpdateOwnerStatusAsync ─────────────────────────────

        [Fact]
        public async Task UpdateOwnerStatusAsync_Rejects_Undefined_Enum_Value()
        {
            var (db, restaurant, _, customer) = await SeedAsync();
            var order = new Order
            {
                OrderNumber = "ORD-TEST-000001",
                Status = OrderStatus.Paid,
                CustomerId = customer.Id,
                RestaurantId = restaurant.Id,
            };
            db.Orders.Add(order);
            await db.SaveChangesAsync();
            var sut = new OrderService(db);

            var result = await sut.UpdateOwnerStatusAsync(order.Id, restaurant.OwnerId, (OrderStatus)99);

            result.Success.Should().BeFalse();
            result.FailureReason.Should().Be(StatusUpdateFailureReason.InvalidStatus);
        }

        [Fact]
        public async Task UpdateOwnerStatusAsync_Rejects_Order_Belonging_To_A_Different_Owner()
        {
            var (db, restaurant, _, customer) = await SeedAsync();
            var order = new Order
            {
                OrderNumber = "ORD-TEST-000001",
                Status = OrderStatus.Paid,
                CustomerId = customer.Id,
                RestaurantId = restaurant.Id,
            };
            db.Orders.Add(order);
            await db.SaveChangesAsync();
            var sut = new OrderService(db);

            var result = await sut.UpdateOwnerStatusAsync(order.Id, "someone-elses-id", OrderStatus.Preparing);

            result.Success.Should().BeFalse();
            result.FailureReason.Should().Be(StatusUpdateFailureReason.NotFound);
        }

        [Fact]
        public async Task UpdateOwnerStatusAsync_Rejects_Transition_Not_On_The_Whitelist()
        {
            // V-05 修復的核心情境：跳過付款直接轉完成
            var (db, restaurant, _, customer) = await SeedAsync();
            var order = new Order
            {
                OrderNumber = "ORD-TEST-000001",
                Status = OrderStatus.PendingPayment,
                CustomerId = customer.Id,
                RestaurantId = restaurant.Id,
            };
            db.Orders.Add(order);
            await db.SaveChangesAsync();
            var sut = new OrderService(db);

            var result = await sut.UpdateOwnerStatusAsync(order.Id, restaurant.OwnerId, OrderStatus.Completed);

            result.Success.Should().BeFalse();
            result.FailureReason.Should().Be(StatusUpdateFailureReason.InvalidTransition);
        }

        [Fact]
        public async Task UpdateOwnerStatusAsync_Allows_A_Whitelisted_Transition()
        {
            var (db, restaurant, _, customer) = await SeedAsync();
            var order = new Order
            {
                OrderNumber = "ORD-TEST-000001",
                Status = OrderStatus.Paid,
                CustomerId = customer.Id,
                RestaurantId = restaurant.Id,
            };
            db.Orders.Add(order);
            await db.SaveChangesAsync();
            var sut = new OrderService(db);

            var result = await sut.UpdateOwnerStatusAsync(order.Id, restaurant.OwnerId, OrderStatus.Preparing);

            result.Success.Should().BeTrue();
            result.Order!.Status.Should().Be(OrderStatus.Preparing);
        }
    }
}
