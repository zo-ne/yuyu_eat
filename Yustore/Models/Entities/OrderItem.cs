using Yustore.Models.Entities;

namespace Yustore.Models.Entities
{
    public class OrderItem
    {
        public int Id { get; set; }
        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; }  // 下單當下的價格快照
        public decimal Subtotal { get; set; }

        // FK
        public int OrderId { get; set; }
        public Order Order { get; set; } = null!;

        public int MenuItemId { get; set; }
        public MenuItem MenuItem { get; set; } = null!;
    }
}