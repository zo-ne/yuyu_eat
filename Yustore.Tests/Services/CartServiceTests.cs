using FluentAssertions;
using Yustore.Services;
using Yustore.Tests.TestHelpers;
using Yustore.ViewModels;

namespace Yustore.Tests.Services
{
    public class CartServiceTests
    {
        private readonly CartService _sut = new();

        [Fact]
        public void AddToCart_Clamps_Quantity_To_At_Least_One()
        {
            // 縱深防禦（V-03 修復）：就算呼叫端沒擋，Service 層自己也要把 <=0 的數量夾到 1，
            // 不能讓負數/零數量的品項進到購物車。
            var context = HttpContextFactory.CreateForUser("user-1");

            _sut.AddToCart(context, new CartItemViewModel { MenuItemId = 1, Name = "便當", Price = 80, Quantity = -1000 },
                restaurantId: 10, restaurantName: "測試店");

            var cart = _sut.GetCart(context);
            cart.Items.Single().Quantity.Should().Be(1);
        }

        [Fact]
        public void AddToCart_Clamps_Quantity_To_At_Most_NinetyNine()
        {
            var context = HttpContextFactory.CreateForUser("user-1");

            _sut.AddToCart(context, new CartItemViewModel { MenuItemId = 1, Name = "便當", Price = 80, Quantity = 999_999_999 },
                restaurantId: 10, restaurantName: "測試店");

            var cart = _sut.GetCart(context);
            cart.Items.Single().Quantity.Should().Be(99);
        }

        [Fact]
        public void AddToCart_Twice_For_Same_Item_Sums_Quantity_But_Still_Clamps()
        {
            var context = HttpContextFactory.CreateForUser("user-1");

            _sut.AddToCart(context, new CartItemViewModel { MenuItemId = 1, Name = "便當", Price = 80, Quantity = 60 },
                restaurantId: 10, restaurantName: "測試店");
            _sut.AddToCart(context, new CartItemViewModel { MenuItemId = 1, Name = "便當", Price = 80, Quantity = 60 },
                restaurantId: 10, restaurantName: "測試店");

            var cart = _sut.GetCart(context);
            cart.Items.Should().ContainSingle(); // 同一個品項合併成一筆，不是變成兩筆
            cart.Items.Single().Quantity.Should().Be(99); // 60+60=120，夾到上限 99
        }

        [Fact]
        public void AddToCart_From_Different_Restaurant_Clears_Previous_Cart()
        {
            // 業務規則：不能同時點兩家店的餐，換店要清空重來
            var context = HttpContextFactory.CreateForUser("user-1");

            _sut.AddToCart(context, new CartItemViewModel { MenuItemId = 1, Name = "A店的便當", Price = 80, Quantity = 1 },
                restaurantId: 10, restaurantName: "A店");
            _sut.AddToCart(context, new CartItemViewModel { MenuItemId = 2, Name = "B店的麵", Price = 60, Quantity = 1 },
                restaurantId: 20, restaurantName: "B店");

            var cart = _sut.GetCart(context);
            cart.RestaurantId.Should().Be(20);
            cart.Items.Should().ContainSingle(i => i.Name == "B店的麵");
        }

        [Fact]
        public void UpdateQuantity_With_Zero_Removes_Item()
        {
            var context = HttpContextFactory.CreateForUser("user-1");
            _sut.AddToCart(context, new CartItemViewModel { MenuItemId = 1, Name = "便當", Price = 80, Quantity = 2 },
                restaurantId: 10, restaurantName: "測試店");

            _sut.UpdateQuantity(context, menuItemId: 1, quantity: 0);

            _sut.GetCart(context).Items.Should().BeEmpty();
        }

        [Fact]
        public void UpdateQuantity_Above_Limit_Clamps_To_NinetyNine()
        {
            var context = HttpContextFactory.CreateForUser("user-1");
            _sut.AddToCart(context, new CartItemViewModel { MenuItemId = 1, Name = "便當", Price = 80, Quantity = 1 },
                restaurantId: 10, restaurantName: "測試店");

            _sut.UpdateQuantity(context, menuItemId: 1, quantity: 500);

            _sut.GetCart(context).Items.Single().Quantity.Should().Be(99);
        }

        [Fact]
        public void Cart_Is_Isolated_Per_User_Within_The_Same_Session()
        {
            // V-09 修復的迴歸測試：原本 Session Key 是固定字串，同一個瀏覽器 Session
            // 換人登入會繼承前一位使用者的購物車。現在 Key 綁定使用者 Id，就算共用同一個
            // Session store，不同使用者的購物車也不會互相看到。
            var sharedSession = new TestSession();
            var userA = HttpContextFactory.CreateForUser("user-a", sharedSession);
            var userB = HttpContextFactory.CreateForUser("user-b", sharedSession);

            _sut.AddToCart(userA, new CartItemViewModel { MenuItemId = 1, Name = "A的餐點", Price = 80, Quantity = 1 },
                restaurantId: 10, restaurantName: "測試店");

            _sut.GetCart(userB).Items.Should().BeEmpty();
            _sut.GetCart(userA).Items.Should().ContainSingle();
        }

        [Fact]
        public void ClearCart_Only_Clears_The_Current_Users_Cart()
        {
            var sharedSession = new TestSession();
            var userA = HttpContextFactory.CreateForUser("user-a", sharedSession);
            var userB = HttpContextFactory.CreateForUser("user-b", sharedSession);

            _sut.AddToCart(userA, new CartItemViewModel { MenuItemId = 1, Name = "A的餐點", Price = 80, Quantity = 1 },
                restaurantId: 10, restaurantName: "測試店");
            _sut.AddToCart(userB, new CartItemViewModel { MenuItemId = 2, Name = "B的餐點", Price = 60, Quantity = 1 },
                restaurantId: 20, restaurantName: "另一家店");

            _sut.ClearCart(userA);

            _sut.GetCart(userA).Items.Should().BeEmpty();
            _sut.GetCart(userB).Items.Should().ContainSingle(); // B 的購物車不該被 A 的 Logout 波及
        }
    }
}
