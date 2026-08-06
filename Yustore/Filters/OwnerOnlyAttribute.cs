using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Yustore.Enums;
using Yustore.Models.Entities;
using Microsoft.AspNetCore.Identity;

namespace Yustore.Filters
{
    // ActionFilter = 在 Action 執行「前後」插入自訂邏輯
    // 這裡用來檢查：這個使用者是不是老闆？
    public class OwnerOnlyAttribute : ActionFilterAttribute
    {
        public override async Task OnActionExecutionAsync(
            ActionExecutingContext context,
            ActionExecutionDelegate next)
        {
            // 取得目前登入的使用者
            var userManager = context.HttpContext.RequestServices
                .GetRequiredService<UserManager<ApplicationUser>>();

            var user = await userManager.GetUserAsync(context.HttpContext.User);

            // 不是老闆就踢回首頁
            if (user == null || user.Role != UserRole.老闆)
            {
                context.Result = new RedirectToActionResult("Index", "Home", null);
                return;
            }

            await next(); // 是老闆，繼續執行
        }
    }
}