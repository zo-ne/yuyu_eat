using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Yustore.Models.Entities;
using Yustore.Services;
using Yustore.ViewModels;
using Yustore.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;

namespace Yustore.Controllers
{
    [AllowAnonymous]
    public class AccountController : Controller
    {
        // 依賴注入（Dependency Injection）
        // 不用自己 new 物件，ASP.NET 會自動幫你建立並傳進來
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly IEmailQueue _emailQueue;

        public AccountController(
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            IEmailQueue emailQueue)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _emailQueue = emailQueue;
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
        [EnableRateLimiting("AuthPolicy")] // V-07 修復：擋掉大量灌註冊、大量寄驗證信的濫用
        public async Task<IActionResult> Register(RegisterViewModel model)
        {
            // ModelState.IsValid：檢查 ViewModel 上的驗證註解是否都通過
            // 例如：Email 格式對不對、密碼夠不夠長
            if (!ModelState.IsValid)
                return View(model); // 驗證失敗，把資料還給頁面顯示錯誤

            // V-08 修復：原本任何人只要有 Email，就能自己註冊成「老闆」開店賣東西，
            // 或註冊成「外送師」看到所有待取餐訂單的顧客姓名與外送地址——
            // 這在真實外送平台是不可能的，店家要營業登記、外送員要身分驗證，也是個資外洩途徑。
            // 這裡先做簡單版：老闆／外送師註冊後帳號預設不啟用（IsActive = false），
            // RoleRequiredAttribute 會擋下未啟用帳號進入 Owner/Driver 的所有功能，
            // 要等待審核（目前只能由後端人員手動開通，Admin 審核後台排在後續里程碑）才能真正使用。
            // 顧客不受影響，註冊後可以馬上用。
            var user = new ApplicationUser
            {
                UserName = model.Email,
                Email = model.Email,
                FullName = model.FullName,
                Role = model.Role,
                IsActive = model.Role == UserRole.Customer
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

                // V-13 修復：FullName 是使用者自由輸入的欄位，直接內插進 HTML 信件會讓攻擊者
                // 把姓名設成 <a href="...">釣魚連結</a>，平台自己的網域就變成發信管道。
                // confirmUrl 是伺服器用 Url.Action 產生的，不是使用者輸入，不需要編碼。
                var safeFullName = System.Net.WebUtility.HtmlEncode(user.FullName);

                // M3 修復（P-05）：丟進佇列就回應，不用等 SMTP 連線/寄送完成，
                // 註冊表單送出的速度不會被 Gmail SMTP 的延遲拖累。
                await _emailQueue.EnqueueAsync(new EmailMessage(
                    user.Email,
                    "【YUYUEAT】請驗證您的 Email",
                    $@"<h2>歡迎加入YUYUEAT！</h2>
                       <p>親愛的 {safeFullName}，</p>
                       <p>請點擊下方連結完成 Email 驗證：</p>
                       <a href='{confirmUrl}'
                          style='background:#ff6b35;color:white;padding:10px 20px;
                                 text-decoration:none;border-radius:5px;'>
                          驗證我的帳號
                       </a>
                       <p>若您沒有註冊YUYUEAT，請忽略此信。</p>"
                ));

                // 跳到「請去收信」提示頁面
                TempData["Message"] = user.IsActive
                    ? $"註冊成功！驗證信已寄到 {user.Email}，請去收信並點擊驗證連結。"
                    : $"註冊成功！驗證信已寄到 {user.Email}，請先去收信驗證 Email。" +
                      "驗證完成後，您的帳號還需要等待平台審核通過，才能使用老闆／外送師的功能。";
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
        [EnableRateLimiting("AuthPolicy")] // V-07 修復：擋掉密碼字典攻擊的大量請求
        public async Task<IActionResult> Login(LoginViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            // PasswordSignInAsync：驗證帳號密碼並建立登入 Cookie
            // V-07 修復：lockoutOnFailure 原本明確關閉，Identity 內建的鎖定機制形同虛設，
            // 密碼可以無限次線上暴力破解。搭配 Program.cs 設定的 5 次/15 分鐘鎖定原則打開它。
            var result = await _signInManager.PasswordSignInAsync(
                model.Email,
                model.Password,
                model.RememberMe,  // 記住我：Cookie 保留時間較長
                lockoutOnFailure: true
            );

            if (result.Succeeded)
            {
                // 登入成功，依角色跳到不同頁面
                var user = await _userManager.FindByEmailAsync(model.Email);

                // V-08 修復：老闆／外送師帳號如果還沒被審核通過（IsActive = false），
                // 直接導去 Owner/Driver 首頁只會被 RoleRequiredAttribute 擋成一個生硬的 403。
                // 這裡先攔下來，給一個看得懂在等什麼的訊息。
                if (user!.Role != UserRole.Customer && !user.IsActive)
                {
                    var roleName = user.Role == UserRole.Owner ? "老闆" : "外送師";
                    TempData["Message"] = $"您的{roleName}帳號正在等待平台審核，審核通過後即可使用相關功能。";
                    return RedirectToAction("Index", "Home");
                }

                return user.Role switch
                {
                    UserRole.Owner => RedirectToAction("Index", "Owner"),
                    UserRole.Driver => RedirectToAction("Index", "Driver"),
                    UserRole.Customer => RedirectToAction("Index", "Customer"),
                    _ => RedirectToAction("Index", "Home")
                };
            }

            if (result.IsLockedOut)
            {
                // V-07 修復：帳號被鎖定時要講清楚，不要跟「密碼打錯」混在一起，
                // 不然使用者會一直重試密碼，鎖定時間反而一直被延長。
                ModelState.AddModelError(string.Empty, "登入失敗次數過多，帳號已暫時鎖定，請 15 分鐘後再試。");
            }
            else if (result.IsNotAllowed)
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

            // V-09 修復：原本只登出 Identity Cookie，沒清 Session。
            // 購物車存在 Session 裡，同一台電腦、同一瀏覽器換人登入會看到前一位使用者的購物車。
            HttpContext.Session.Clear();

            return RedirectToAction("Index", "Home");
        }
    }
}