using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Yustore.Models.Entities;

namespace Yustore.Services
{
    // M3 修復（P-03）：原本 _Layout.cshtml 每個頁面都自己再查一次 UserManager.GetUserAsync()
    // 只為了顯示姓名/Email/角色選單，而 RoleRequiredAttribute 才剛查過一次——一個頁面至少
    // 兩次多餘的使用者查詢。把這幾個「登入時就決定好、使用者不會自己隨時改」的欄位放進
    // Claims，畫面直接讀 Claims，零 DB 查詢。
    //
    // 這幾個 Claims 是在登入/Cookie 重新整理（Identity 的 SecurityStampValidator 預設每 30
    // 分鐘跑一次）當下產生的快照：改名字或角色不會立刻反映在「已登入中」的分頁，要等下次
    // 登入或下一次 Cookie 重新整理。IsActive 停權狀態的即時性需求不一樣（被停權要馬上生效），
    // 所以那個檢查刻意沒有放進 Claims，RoleRequiredAttribute 仍然會即時查 DB 驗證。
    public class AppClaimsPrincipalFactory : UserClaimsPrincipalFactory<ApplicationUser, IdentityRole>
    {
        public AppClaimsPrincipalFactory(
            UserManager<ApplicationUser> userManager,
            RoleManager<IdentityRole> roleManager,
            IOptions<IdentityOptions> options)
            : base(userManager, roleManager, options)
        {
        }

        public override async Task<ClaimsPrincipal> CreateAsync(ApplicationUser user)
        {
            var principal = await base.CreateAsync(user);
            var identity = (ClaimsIdentity)principal.Identity!;

            identity.AddClaim(new Claim(ClaimTypes.Role, user.Role.ToString()));
            identity.AddClaim(new Claim(AppClaimTypes.FullName, user.FullName));

            if (!string.IsNullOrEmpty(user.Email))
                identity.AddClaim(new Claim(ClaimTypes.Email, user.Email));

            return principal;
        }
    }

    public static class AppClaimTypes
    {
        public const string FullName = "Yustore:FullName";
    }
}
