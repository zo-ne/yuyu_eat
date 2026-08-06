using Yustore.Enums;
using Yustore.Models.Entities;

namespace Yustore.Models.Entities
{
    public class Settlement
    {
        public int Id { get; set; }
        public int Year { get; set; }
        public int Month { get; set; }
        public decimal FoodAmount { get; set; }    // 應付老闆的餐費
        public SettlementStatus Status { get; set; } = SettlementStatus.未結算;
        public DateTime? SettledAt { get; set; }
        public string? Note { get; set; }

        // FK
        public int OrderId { get; set; }
        public Order Order { get; set; } = null!;

        public string DriverId { get; set; } = string.Empty;
        public ApplicationUser Driver { get; set; } = null!;

        public string OwnerId { get; set; } = string.Empty;
        public ApplicationUser Owner { get; set; } = null!;
    }
}