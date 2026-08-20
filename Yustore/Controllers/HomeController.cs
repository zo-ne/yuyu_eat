using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Yustore.Models;

namespace Yustore.Controllers
{
    [AllowAnonymous]
    public class HomeController : Controller
    {
        public IActionResult Index()
        {
            // 暫時把首頁導向登入頁
            return RedirectToAction("Login", "Account");
        }

        // V-12 修復：Program.cs 在非開發環境會用 UseExceptionHandler("/Home/Error") 導向這裡，
        // 但這個 action 原本不存在，導致上線後任何未處理例外都變成 404，而不是顯示錯誤頁。
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel
            {
                RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier
            });
        }
    }
}