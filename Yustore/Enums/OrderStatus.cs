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
        Owner = 2
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
}
