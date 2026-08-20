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

        // M4 修復（V-14）：這個欄位以前定義了、存進資料庫了，但沒有任何程式碼讀取它。
        // 現在 RoleRequiredAttribute 會檢查，Admin 可以用它停權任何角色的帳號。
        public bool IsActive { get; set; } = true;

        // M4 新增：老闆／外送師的申請審核狀態，顧客註冊時直接是 Approved（不用審核）。
        // 跟 IsActive 是獨立的兩個欄位，見 Yustore.Enums.ApplicationStatus 上的說明。
        public ApplicationStatus ApplicationStatus { get; set; } = ApplicationStatus.Approved;

        // Admin 拒絕申請時可以附理由，顯示給申請人看
        public string? ApplicationRejectionReason { get; set; }

        // 導覽屬性（Navigation Properties）
        // 這些不是資料庫欄位，而是讓 EF Core 知道「關聯」用的
        // 例如：user.CustomerOrders 可以直接拿到這個用戶的所有訂單
        public Restaurant? Restaurant { get; set; }
        public ICollection<Order> CustomerOrders { get; set; } = new List<Order>();
        public ICollection<Review> ReviewsGiven { get; set; } = new List<Review>();
        public ICollection<Review> ReviewsReceived { get; set; } = new List<Review>();
    }
}