using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Yustore.Models.Entities;
using Yustore.Services;
using Yustore.ViewModels;
using Yustore.Enums;
using Microsoft.AspNetCore.Authorization;

namespace Yustore.Controllers
{
    [AllowAnonymous]
    public class AccountController : Controller
    {
        // 依賴注入（Dependency Injection）
        // 不用自己 new 物件，ASP.NET 會自動幫你建立並傳進來
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly IEmailService _emailService;

        public AccountController(
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            IEmailService emailService)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _emailService = emailService;
        }

        // ────────────────────────────────────────
        // 註冊
        // ────────────────────────────────────────

        // GET: /Account/Register
        // 使用者進入註冊頁面時執行，只是顯示空白表單
        [HttpGet]
        public IActionResult Register()
        {
            return View();
        }

        // POST: /Account/Register
        // 使用者按下「註冊」送出表單時執行
        [HttpPost]
        [ValidateAntiForgeryToken] // 防止跨站攻擊（CSRF），表單必須從我們的頁面送出
        public async Task<IActionResult> Register(RegisterViewModel model)
        {
            // ModelState.IsValid：檢查 ViewModel 上的驗證註解是否都通過
            // 例如：Email 格式對不對、密碼夠不夠長
            if (!ModelState.IsValid)
                return View(model); // 驗證失敗，把資料還給頁面顯示錯誤

            // 建立新使用者
            var user = new ApplicationUser
            {
                UserName = model.Email,
                Email = model.Email,
                FullName = model.FullName,
                Role = model.Role
            };

            // CreateAsync：建立使用者並把密碼雜湊後存入資料庫
            // 不會明文存密碼！Identity 會自動幫你加密
            var result = await _userManager.CreateAsync(user, model.Password);

            if (result.Succeeded)
            {
                // 產生 Email 驗證 Token（一組亂數字串）
                var token = await _userManager.GenerateEmailConfirmationTokenAsync(user);

                // 產生驗證連結（包含 userId 和 token）
                var confirmUrl = Url.Action(
                    "ConfirmEmail",      // Action 名稱
                    "Account",           // Controller 名稱
                    new { userId = user.Id, token = token }, // 參數
                    Request.Scheme       // http 或 https
                );

                // 寄驗證信
                await _emailService.SendEmailAsync(
                    user.Email,
                    "【YUYUEAT】請驗證您的 Email",
                    $@"<h2>歡迎加入YUYUEAT！</h2>
                       <p>親愛的 {user.FullName}，</p>
                       <p>請點擊下方連結完成 Email 驗證：</p>
                       <a href='{confirmUrl}' 
                          style='background:#ff6b35;color:white;padding:10px 20px;
                                 text-decoration:none;border-radius:5px;'>
                          驗證我的帳號
                       </a>
                       <p>若您沒有註冊YUYUEAT，請忽略此信。</p>"
                );

                // 跳到「請去收信」提示頁面
                TempData["Message"] = $"註冊成功！驗證信已寄到 {user.Email}，請去收信並點擊驗證連結。";
                return RedirectToAction("RegisterSuccess");
            }

            // 如果建立失敗（例如 Email 已被使用），把錯誤顯示在頁面上
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }

            return View(model);
        }

        // GET: /Account/RegisterSuccess
        // 顯示「請去收信」的提示頁面
        [HttpGet]
        public IActionResult RegisterSuccess()
        {
            return View();
        }

        // GET: /Account/ConfirmEmail?userId=xxx&token=xxx
        // 使用者點驗證信裡的連結時執行
        [HttpGet]
        public async Task<IActionResult> ConfirmEmail(string userId, string token)
        {
            if (userId == null || token == null)
                return RedirectToAction("Index", "Home");

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
                return NotFound();

            // 用 token 驗證 Email
            var result = await _userManager.ConfirmEmailAsync(user, token);

            if (result.Succeeded)
            {
                TempData["Message"] = "Email 驗證成功！請登入。";
                return RedirectToAction("Login","Account");
            }

            TempData["Error"] = "驗證連結無效或已過期。";
            return RedirectToAction("Login");
        }

        // ────────────────────────────────────────
        // 登入
        // ────────────────────────────────────────

        // GET: /Account/Login
        [HttpGet]
        public IActionResult Login()
        {
            return View();
        }

        // POST: /Account/Login
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            // PasswordSignInAsync：驗證帳號密碼並建立登入 Cookie
            var result = await _signInManager.PasswordSignInAsync(
                model.Email,
                model.Password,
                model.RememberMe,  // 記住我：Cookie 保留時間較長
                lockoutOnFailure: false // 登入失敗不鎖帳號（可依需求開啟）
            );

            if (result.Succeeded)
            {
                // 登入成功，依角色跳到不同頁面
                var user = await _userManager.FindByEmailAsync(model.Email);

                return user!.Role switch
                {
                    UserRole.老闆 => RedirectToAction("Index", "Owner"),
                    UserRole.外送師 => RedirectToAction("Index", "Driver"),
                    UserRole.顧客 => RedirectToAction("Index", "Customer"),
                    _ => RedirectToAction("Index", "Home")
                };
            }

            if (result.IsNotAllowed)
            {
                // Email 還沒驗證
                ModelState.AddModelError(string.Empty, "請先驗證您的 Email 後再登入。");
            }
            else
            {
                ModelState.AddModelError(string.Empty, "Email 或密碼錯誤。");
            }

            return View(model);
        }

        // ────────────────────────────────────────
        // 登出
        // ────────────────────────────────────────

        // POST: /Account/Logout
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await _signInManager.SignOutAsync();
            return RedirectToAction("Index", "Home");
        }
    }
}