using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Yustore.Enums;
using Yustore.Models.Entities;

namespace Yustore.Filters
{
    // V-14 / P-07 修復：CustomerOnlyAttribute / OwnerOnlyAttribute / DriverOnlyAttribute
    // 原本是同一份程式碼複製三次，只差一個 enum 值。合併成這個帶參數的 Filter，
    // 用法：[RoleRequired(UserRole.Owner)]。
    //
    // 同時修復：
    // - V-14：ApplicationUser.IsActive 這個停權欄位定義了、存進資料庫了，
    //   但原本三個 Filter 都沒有讀取它，被停權的帳號照樣能正常使用。這裡補上檢查。
    // - 回傳語意：原本用 302 Redirect 到首頁，語意上應該是 403（你登入了，但沒有權限），
    //   改回 Forbid()，以後要拆 REST API 時語意才是對的。
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public class RoleRequiredAttribute : ActionFilterAttribute
    {
        private readonly UserRole _role;

        public RoleRequiredAttribute(UserRole role)
        {
            _role = role;
        }

        public override async Task OnActionExecutionAsync(
            ActionExecutingContext context,
            ActionExecutionDelegate next)
        {
            var userManager = context.HttpContext.RequestServices
                .GetRequiredService<UserManager<ApplicationUser>>();

            var user = await userManager.GetUserAsync(context.HttpContext.User);

            if (user == null || user.Role != _role || !user.IsActive)
            {
                context.Result = new ForbidResult();
                return;
            }

            await next();
        }
    }
}
