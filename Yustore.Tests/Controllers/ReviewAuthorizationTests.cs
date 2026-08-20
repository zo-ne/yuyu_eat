using FluentAssertions;
using Yustore.Controllers;
using Yustore.Enums;
using Yustore.Models.Entities;

namespace Yustore.Tests.Controllers
{
    // 評價授權判定（ASSESSMENT.md 建議優先測的項目之一）：
    // ReviewController.GetReviewTargets / GetAllRequiredReviewPairs 是 V-01（越權評分）
    // 跟 R-4（訂單要全部評完才轉「完成」）兩個修復的核心邏輯，用 internal + InternalsVisibleTo
    // 直接測，不用為了測試特地把私有邏輯改成 public。
    public class ReviewAuthorizationTests
    {
        private static ApplicationUser MakeUser(string id, string name) =>
            new() { Id = id, FullName = name };

        private static Order MakeOrderWithDriver()
        {
            var customer = MakeUser("customer-1", "小明");
            var owner = MakeUser("owner-1", "店長");
            var driver = MakeUser("driver-1", "外送員");

            var order = new Order
            {
                CustomerId = customer.Id,
                Customer = customer,
                RestaurantId = 1,
                Restaurant = new Restaurant { OwnerId = owner.Id, Owner = owner },
            };
            order.Delivery = new Delivery { OrderId = order.Id, Order = order, DriverId = driver.Id, Driver = driver };

            return order;
        }

        private static Order MakeOrderWithoutDriver()
        {
            var customer = MakeUser("customer-1", "小明");
            var owner = MakeUser("owner-1", "店長");

            return new Order
            {
                CustomerId = customer.Id,
                Customer = customer,
                RestaurantId = 1,
                Restaurant = new Restaurant { OwnerId = owner.Id, Owner = owner },
            };
        }

        [Fact]
        public void GetReviewTargets_For_Customer_Returns_Owner_And_Driver()
        {
            var order = MakeOrderWithDriver();

            var targets = ReviewController.GetReviewTargets(order, order.Customer);

            targets.Should().BeEquivalentTo(new[]
            {
                (TargetUserId: "owner-1", TargetUserName: "店長", TargetType: ReviewTargetType.Owner),
                (TargetUserId: "driver-1", TargetUserName: "外送員", TargetType: ReviewTargetType.Driver),
            });
        }

        [Fact]
        public void GetReviewTargets_For_Driver_Returns_Customer_And_Owner()
        {
            var order = MakeOrderWithDriver();

            var targets = ReviewController.GetReviewTargets(order, order.Delivery!.Driver);

            targets.Should().BeEquivalentTo(new[]
            {
                (TargetUserId: "customer-1", TargetUserName: "小明", TargetType: ReviewTargetType.Customer),
                (TargetUserId: "owner-1", TargetUserName: "店長", TargetType: ReviewTargetType.Owner),
            });
        }

        [Fact]
        public void GetReviewTargets_For_Owner_Without_Driver_Only_Returns_Customer()
        {
            var order = MakeOrderWithoutDriver();

            var targets = ReviewController.GetReviewTargets(order, order.Restaurant.Owner);

            targets.Should().BeEquivalentTo(new[]
            {
                (TargetUserId: "customer-1", TargetUserName: "小明", TargetType: ReviewTargetType.Customer),
            });
        }

        [Fact]
        public void GetReviewTargets_For_Unrelated_User_Returns_Empty()
        {
            // V-01 修復的核心迴歸測試：攻擊者不是這筆訂單的顧客/老闆/外送員，
            // 合法評分對象清單必須是空的——ValidateReviewRequestAsync 就是靠這份空清單判斷要 Forbid()。
            var order = MakeOrderWithDriver();
            var stranger = MakeUser("stranger-1", "路人");

            ReviewController.GetReviewTargets(order, stranger).Should().BeEmpty();
        }

        [Fact]
        public void GetAllRequiredReviewPairs_With_Driver_Returns_All_Six_Pairs()
        {
            var order = MakeOrderWithDriver();

            var pairs = ReviewController.GetAllRequiredReviewPairs(order);

            pairs.Should().BeEquivalentTo(new[]
            {
                ("customer-1", "owner-1"),
                ("owner-1", "customer-1"),
                ("customer-1", "driver-1"),
                ("driver-1", "customer-1"),
                ("driver-1", "owner-1"),
                ("owner-1", "driver-1"),
            });
        }

        [Fact]
        public void GetAllRequiredReviewPairs_Without_Driver_Only_Requires_Customer_Owner_Pair()
        {
            var order = MakeOrderWithoutDriver();

            var pairs = ReviewController.GetAllRequiredReviewPairs(order);

            pairs.Should().BeEquivalentTo(new[]
            {
                ("customer-1", "owner-1"),
                ("owner-1", "customer-1"),
            });
        }
    }
}
