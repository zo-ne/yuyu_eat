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
    // - V-14：ApplicationUser.IsActive 這個停權欄位定義了、存進資料庫了，
    //   但原本三個 Filter 都沒有讀取它，被停權的帳號照樣能正常使用。這裡補上檢查。
    // - 回傳語意：原本用 302 Redirect 到首頁，語意上應該是 403（你登入了，但沒有權限），
    //   改回 Forbid()，以後要拆 REST API 時語意才是對的。
    // - M3 修復（P-03）：角色改成先讀 Claims（AppClaimsPrincipalFactory 在登入時放進去的），
    //   零 DB 查詢就能擋掉「角色不對」這個最常見的情況。只有角色對得上時才需要查一次 DB，
    //   目的是驗證 IsActive——這個狀態可能隨時被管理員改變，Claims 是登入當下的快照，
    //   沒辦法保證即時，所以停權檢查刻意不走 Claims，寧可多這一次查詢也要確保即時生效。
    // - M4 修復（V-08）：老闆/外送師還要另外檢查 ApplicationStatus 是不是 Approved——
    //   註冊只代表送出申請，要 Admin 審核通過才能真的使用這個角色的功能。
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
            if (!context.HttpContext.User.IsInRole(_role.ToString()))
            {
                context.Result = new ForbidResult();
                return;
            }

            var userManager = context.HttpContext.RequestServices
                .GetRequiredService<UserManager<ApplicationUser>>();

            var user = await userManager.GetUserAsync(context.HttpContext.User);

            if (user == null || !user.IsActive)
            {
                context.Result = new ForbidResult();
                return;
            }

            // 顧客免審核；老闆/外送師/管理員都需要 ApplicationStatus 是 Approved 才能用該角色的功能
            if (_role != UserRole.Customer && user.ApplicationStatus != ApplicationStatus.Approved)
            {
                context.Result = new ForbidResult();
                return;
            }

            await next();
        }
    }
}
