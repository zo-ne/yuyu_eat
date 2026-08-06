using System.ComponentModel.DataAnnotations;

namespace Yustore.ViewModels
{
    // 新增/編輯餐點用的 ViewModel
    public class MenuItemViewModel
    {
        public int Id { get; set; } // 編輯時用，新增時為 0

        [Required(ErrorMessage = "請輸入餐點名稱")]
        [Display(Name = "餐點名稱")]
        public string Name { get; set; } = string.Empty;

        [Display(Name = "餐點描述")]
        public string? Description { get; set; }

        [Required(ErrorMessage = "請輸入價格")]
        [Range(1, 99999, ErrorMessage = "價格需在 1~99999 之間")]
        [Display(Name = "價格")]
        public decimal Price { get; set; }

        [Display(Name = "餐點圖片")]
        // IFormFile = 使用者上傳的檔案
        public IFormFile? ImageFile { get; set; }

        // 顯示目前圖片用（編輯時）
        public string? CurrentImageUrl { get; set; }

        [Display(Name = "是否供應")]
        public bool IsAvailable { get; set; } = true;
    }
}