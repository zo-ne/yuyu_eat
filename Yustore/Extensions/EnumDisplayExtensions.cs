using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace Yustore.Extensions
{
    // enum 改英文命名之後（見 Yustore.Enums），Razor 畫面不能再直接 @Model.Status
    // 顯示中文了（會印出 "Paid" 而不是 "已付款"）。這個擴充方法讀 [Display(Name = "...")]
    // 把中文顯示名稱找回來；找不到就退回 enum 的 ToString()，不會整頁掛掉。
    public static class EnumDisplayExtensions
    {
        public static string GetDisplayName(this Enum value)
        {
            var field = value.GetType().GetField(value.ToString());
            var attr = field?.GetCustomAttribute<DisplayAttribute>();
            return attr?.Name ?? value.ToString();
        }
    }
}
