using System.ComponentModel.DataAnnotations;
using Yustore.Enums;

namespace Yustore.ViewModels
{
    // ViewModel 上的 [屬性] 叫做「資料驗證註解」(Data Annotations)
    // ASP.NET 會自動幫你驗證，不用自己寫 if 判斷
    public class RegisterViewModel
    {
        [Required(ErrorMessage = "請輸入姓名")]
        [Display(Name = "姓名")]
        public string FullName { get; set; } = string.Empty;

        [Required(ErrorMessage = "請輸入 Email")]
        [EmailAddress(ErrorMessage = "Email 格式不正確")]
        [Display(Name = "Email")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "請輸入密碼")]
        [StringLength(100, MinimumLength = 8, ErrorMessage = "密碼至少 8 個字元")]
        [DataType(DataType.Password)]
        [Display(Name = "密碼")]
        public string Password { get; set; } = string.Empty;

        [Required(ErrorMessage = "請確認密碼")]
        [Compare("Password", ErrorMessage = "兩次密碼不一致")]
        [DataType(DataType.Password)]
        [Display(Name = "確認密碼")]
        public string ConfirmPassword { get; set; } = string.Empty;

        [Required(ErrorMessage = "請選擇身份")]
        [Display(Name = "我的身份")]
        public UserRole Role { get; set; }
    }
}