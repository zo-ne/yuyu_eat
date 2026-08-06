using Microsoft.AspNetCore.Identity;
using Yustore.Enums;

namespace Yustore.Models.Entities
{
    // ApplicationUser 繼承 IdentityUser
    // IdentityUser 已經有：Id、Email、UserName、PasswordHash 等欄位
    // 我們在這裡「擴充」加上自己需要的欄位
    public class ApplicationUser : IdentityUser
    {
        public string FullName { get; set; } = string.Empty;
        public UserRole Role { get; set; }
        public string? AvatarUrl { get; set; }  // ? 表示可以是 null（選填）
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public bool IsActive { get; set; } = true;

        // 導覽屬性（Navigation Properties）
        // 這些不是資料庫欄位，而是讓 EF Core 知道「關聯」用的
        // 例如：user.CustomerOrders 可以直接拿到這個用戶的所有訂單
        public Restaurant? Restaurant { get; set; }
        public ICollection<Order> CustomerOrders { get; set; } = new List<Order>();
        public ICollection<Review> ReviewsGiven { get; set; } = new List<Review>();
        public ICollection<Review> ReviewsReceived { get; set; } = new List<Review>();
    }
}