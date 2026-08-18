using Yustore.Models.Entities;

namespace Yustore.Models.Entities
{
    public class OrderItem
    {
        public int Id { get; set; }
        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; }  // 下單當下的價格快照
        public decimal Subtotal { get; set; }

        // 下單當下的餐點名稱快照。V-02 修復的一部分：
        // 就算之後 MenuItem 被刪除（或改名），歷史訂單明細仍能正確顯示品項名稱。
        public string MenuItemName { get; set; } = string.Empty;

        // FK
        public int OrderId { get; set; }
        public Order Order { get; set; } = null!;

        public int MenuItemId { get; set; }
        public MenuItem MenuItem { get; set; } = null!;
    }
}