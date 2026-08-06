namespace Yustore.ViewModels
{
    // 購物車裡的單一項目
    public class CartItemViewModel
    {
        public int MenuItemId { get; set; }
        public string Name { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public int Quantity { get; set; }
        public string? ImageUrl { get; set; }

        // 計算小計（這個屬性不用 set，自動計算）
        public decimal Subtotal => Price * Quantity;
    }
}