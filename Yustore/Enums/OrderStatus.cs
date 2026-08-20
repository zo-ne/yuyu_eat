using System.ComponentModel.DataAnnotations;

namespace Yustore.Enums
{
    // 數值刻意保持跟改名前一致（0~7），只是把識別字從中文改成英文。
    // 不影響資料庫裡已經存的 int 值，不需要另外跑 migration。
    // 中文顯示名稱透過 [Display(Name = "...")]，搭配 Yustore.Extensions.EnumDisplayExtensions.GetDisplayName() 取出。
    public enum OrderStatus
    {
        [Display(Name = "待付款")]
        PendingPayment = 0,

        [Display(Name = "已付款")]
        Paid = 1,

        [Display(Name = "備餐中")]
        Preparing = 2,

        [Display(Name = "待取餐")]
        ReadyForPickup = 3,

        [Display(Name = "外送中")]
        OutForDelivery = 4,

        [Display(Name = "已送達")]
        Delivered = 5,

        [Display(Name = "完成")]
        Completed = 6,

        [Display(Name = "已取消")]
        Cancelled = 7
    }

    public enum SettlementStatus
    {
        [Display(Name = "未結算")]
        Unsettled = 0,

        [Display(Name = "結算中")]
        Settling = 1,

        [Display(Name = "已結算")]
        Settled = 2
    }

    public enum UserRole
    {
        [Display(Name = "顧客")]
        Customer = 0,

        [Display(Name = "外送師")]
        Driver = 1,

        [Display(Name = "老闆")]
        Owner = 2,

        // M4 新增：加在最後，不能插在中間，不然舊資料的數值語意會全部錯位（見 ASSESSMENT.md §3.3）。
        // Admin 帳號只能透過資料庫 Seed 建立（見 Program.cs），註冊表單不開放這個選項，
        // AccountController.Register 也會明確擋掉有人直接 POST Role=3 想自己升級成 Admin。
        [Display(Name = "管理員")]
        Admin = 3
    }

    public enum ReviewTargetType
    {
        [Display(Name = "老闆")]
        Owner = 0,

        [Display(Name = "外送師")]
        Driver = 1,

        [Display(Name = "顧客")]
        Customer = 2
    }

    // M4 新增：老闆／外送師的申請審核狀態。跟 ApplicationUser.IsActive 是兩個獨立的概念——
    // ApplicationStatus 管「這個身分申請有沒有通過審核」，IsActive 管「這個帳號有沒有被停權」，
    // 兩者互不影響：一個已核准的老闆一樣可能因為違規被停權（IsActive=false），
    // 一個被拒絕的申請人帳號本身不算停權（IsActive 還是 true，只是不能用老闆/外送師功能）。
    public enum ApplicationStatus
    {
        [Display(Name = "審核中")]
        Pending = 0,

        [Display(Name = "已核准")]
        Approved = 1,

        [Display(Name = "已拒絕")]
        Rejected = 2
    }
}
