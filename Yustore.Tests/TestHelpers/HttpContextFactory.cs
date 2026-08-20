using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Yustore.Tests.TestHelpers
{
    internal static class HttpContextFactory
    {
        // 建一個帶有指定登入使用者 Id 與（選填）Session 的 HttpContext，
        // 給 CartService 測試用（CartService 靠 ClaimTypes.NameIdentifier 決定購物車 Key）。
        public static HttpContext CreateForUser(string userId, ISession? session = null)
        {
            var context = new DefaultHttpContext
            {
                Session = session ?? new TestSession(),
            };

            var identity = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, userId)
            }, authenticationType: "Test");

            context.User = new ClaimsPrincipal(identity);

            return context;
        }
    }
}
