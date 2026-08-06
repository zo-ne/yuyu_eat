using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

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
    }
}