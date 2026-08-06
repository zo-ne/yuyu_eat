namespace Yustore.ViewModels
{
    // 整個購物車
    public class CartViewModel
    {
        public int RestaurantId { get; set; }
        public string RestaurantName { get; set; } = string.Empty;
        public List<CartItemViewModel> Items { get; set; } = new();

        // 餐費小計
        public decimal FoodTotal => Items.Sum(i => i.Subtotal);
        // 外送費固定 30
        public decimal DeliveryFee => 30;
        // 總計
        public decimal GrandTotal => FoodTotal + DeliveryFee;
    }
}