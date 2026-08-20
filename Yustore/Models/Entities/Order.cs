using Yustore.Enums;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Yustore.Models.Entities;

namespace Yustore.Models.Entities
{
    public class Order
    {
        public int Id { get; set; }
        public string OrderNumber { get; set; } = string.Empty; // 例如 ORD-20240101-001
        public OrderStatus Status { get; set; } = OrderStatus.PendingPayment;
        public decimal FoodTotal { get; set; }      // 餐費小計
        public decimal DeliveryFee { get; set; } = 30; // 固定外送費
        public decimal GrandTotal { get; set; }     // 總計
        public string? DeliveryAddress { get; set; }
        public string? Note { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime? CompletedAt { get; set; }

        // FK
        public string CustomerId { get; set; } = string.Empty;
        public ApplicationUser Customer { get; set; } = null!;

        public int RestaurantId { get; set; }
        public Restaurant Restaurant { get; set; } = null!;

        public ICollection<OrderItem> OrderItems { get; set; } = new List<OrderItem>();
        public Delivery? Delivery { get; set; }
        public Settlement? Settlement { get; set; }
        public ICollection<Review> Reviews { get; set; } = new List<Review>();
    }
}