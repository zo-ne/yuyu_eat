using FluentAssertions;
using Yustore.ViewModels;

namespace Yustore.Tests.ViewModels
{
    // 購物車金額計算（ASSESSMENT.md 建議優先測的項目之一）
    public class CartViewModelTests
    {
        [Fact]
        public void Subtotal_Is_Price_Times_Quantity()
        {
            var item = new CartItemViewModel { Price = 45.5m, Quantity = 3 };

            item.Subtotal.Should().Be(136.5m);
        }

        [Fact]
        public void FoodTotal_Sums_All_Item_Subtotals()
        {
            var cart = new CartViewModel
            {
                Items =
                {
                    new CartItemViewModel { Price = 100, Quantity = 2 }, // 200
                    new CartItemViewModel { Price = 30, Quantity = 1 },  // 30
                }
            };

            cart.FoodTotal.Should().Be(230m);
        }

        [Fact]
        public void GrandTotal_Is_FoodTotal_Plus_Fixed_DeliveryFee()
        {
            var cart = new CartViewModel
            {
                Items = { new CartItemViewModel { Price = 100, Quantity = 1 } }
            };

            cart.DeliveryFee.Should().Be(30m);
            cart.GrandTotal.Should().Be(130m);
        }

        [Fact]
        public void Empty_Cart_Totals_Are_Zero_Plus_DeliveryFee()
        {
            var cart = new CartViewModel();

            cart.FoodTotal.Should().Be(0m);
            cart.GrandTotal.Should().Be(30m); // 就算沒有任何品項，外送費目前還是固定 30
        }
    }
}
