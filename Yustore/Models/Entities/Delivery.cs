using Yustore.Enums;
using Yustore.Models.Entities;

namespace Yustore.Models.Entities
{
    public class Delivery
    {
        public int Id { get; set; }
        public DateTime? PickedUpAt { get; set; }
        public DateTime? DeliveredAt { get; set; }
        public string? ProofPhotoUrl { get; set; }  // 完成拍照上傳
        public string? Note { get; set; }

        // FK
        public int OrderId { get; set; }
        public Order Order { get; set; } = null!;

        public string DriverId { get; set; } = string.Empty;
        public ApplicationUser Driver { get; set; } = null!;
    }
}