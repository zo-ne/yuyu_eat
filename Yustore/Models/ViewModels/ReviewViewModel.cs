using System.ComponentModel.DataAnnotations;
using Yustore.Enums;

namespace Yustore.ViewModels
{
    public class ReviewViewModel
    {
        public int OrderId { get; set; }
        public string OrderNumber { get; set; } = string.Empty;

        // 要評分的對象資訊
        public string TargetUserId { get; set; } = string.Empty;
        public string TargetUserName { get; set; } = string.Empty;
        public ReviewTargetType TargetType { get; set; }

        [Required(ErrorMessage = "請選擇星等")]
        [Range(1, 5, ErrorMessage = "請選擇 1~5 顆星")]
        [Display(Name = "評分")]
        public int Stars { get; set; }

        [Display(Name = "留言")]
        [StringLength(500, ErrorMessage = "留言不超過 500 字")]
        public string? Comment { get; set; }
    }

    // 一個訂單可能要評多個人，這個 ViewModel 裝「這筆訂單需要評分的所有對象」
    public class OrderReviewViewModel
    {
        public int OrderId { get; set; }
        public string OrderNumber { get; set; } = string.Empty;
        public List<PendingReviewItem> PendingReviews { get; set; } = new();
    }

    // 待評分的單一對象
    public class PendingReviewItem
    {
        public string TargetUserId { get; set; } = string.Empty;
        public string TargetUserName { get; set; } = string.Empty;
        public ReviewTargetType TargetType { get; set; }
        public bool AlreadyReviewed { get; set; } // 已經評過了嗎？
    }
}