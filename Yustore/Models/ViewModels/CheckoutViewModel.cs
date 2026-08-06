using System.ComponentModel.DataAnnotations;

namespace Yustore.ViewModels
{
    // 結帳頁面
    public class CheckoutViewModel
    {
        [Required(ErrorMessage = "請輸入送達地址")]
        [Display(Name = "送達地址")]
        public string DeliveryAddress { get; set; } = string.Empty;

        [Display(Name = "備註")]
        public string? Note { get; set; }

        // 顯示用（從購物車帶過來）
        public CartViewModel? Cart { get; set; }
    }
}