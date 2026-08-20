namespace Yustore.Models.Entities
{
    public class MenuItem
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public decimal Price { get; set; }
        public string? ImageUrl { get; set; }
        public bool IsAvailable { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        // V-02 修復：改用軟刪除，避免歷史訂單明細的 MenuItem 關聯被硬刪除牽連。
        // 查詢一律透過 AppDbContext 的全域篩選器自動排除 IsDeleted = true 的餐點。
        public bool IsDeleted { get; set; } = false;

        // FK
        public int RestaurantId { get; set; }
        public Restaurant Restaurant { get; set; } = null!;

        public ICollection<OrderItem> OrderItems { get; set; } = new List<OrderItem>();
    }
}