using Yustore.Enums;
using Yustore.Models.Entities;

namespace Yustore.Models.Entities
{
    public class Review
    {
        public int Id { get; set; }
        public int Stars { get; set; }          // 1-5
        public string? Comment { get; set; }
        public ReviewTargetType TargetType { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        // FK
        public int OrderId { get; set; }
        public Order Order { get; set; } = null!;

        public string ReviewerId { get; set; } = string.Empty;
        public ApplicationUser Reviewer { get; set; } = null!;

        public string TargetUserId { get; set; } = string.Empty;
        public ApplicationUser TargetUser { get; set; } = null!;
    }
}