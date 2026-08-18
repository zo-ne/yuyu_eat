using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Yustore.Data;
using Yustore.Enums;
using Yustore.Extensions;
using Yustore.Filters;
using Yustore.Models.Entities;
using Yustore.Services;
using Yustore.ViewModels;

namespace Yustore.Controllers
{
    // [RoleRequired(UserRole.Owner)] 套用在整個 Controller
    // 表示這裡所有的 Action 都只有老闆能進入
    [RoleRequired(UserRole.Owner)]
    public class OwnerController : Controller
    {
        private readonly AppDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IImageService _imageService;
        private readonly IEmailService _emailService;

        public OwnerController(
            AppDbContext db,
            UserManager<ApplicationUser> userManager,
            IImageService imageService,
            IEmailService emailService)
        {
            _db = db;
            _userManager = userManager;
            _imageService = imageService;
            _emailService = emailService;
        }

        // ════════════════════════════════════════
        // 首頁：訂單總覽
        // ════════════════════════════════════════

        // GET: /Owner/Index
        public async Task<IActionResult> Index()
        {
            var user = await _userManager.GetUserAsync(User);

            // 取得這個老闆的店家
            var restaurant = await _db.Restaurants
                .FirstOrDefaultAsync(r => r.OwnerId == user!.Id);

            // 如果還沒建立店家，跳到建立店家頁面
            if (restaurant == null)
                return RedirectToAction("SetupRestaurant");

            // 取得最新10筆訂單
            var recentOrders = await _db.Orders
                .Where(o => o.RestaurantId == restaurant.Id)
                .Include(o => o.Customer)   // Include = 同時載入關聯資料
                .Include(o => o.OrderItems)
                .OrderByDescending(o => o.CreatedAt)
                .Take(10)
                .ToListAsync();

            ViewBag.Restaurant = restaurant;
            ViewBag.RecentOrders = recentOrders;

            // 統計數字
            ViewBag.TodayOrderCount = recentOrders
                .Count(o => o.CreatedAt.Date == DateTime.Today);
            ViewBag.PendingOrderCount = recentOrders
                .Count(o => o.Status == OrderStatus.Paid);

            return View();
        }

        // ════════════════════════════════════════
        // 建立店家資料（第一次登入）
        // ════════════════════════════════════════

        [HttpGet]
        public IActionResult SetupRestaurant()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(6_000_000)] // V-06 修復：搭配 ImageService 的 5MB 限制，多留一點給 multipart 表單本身的開銷
        public async Task<IActionResult> SetupRestaurant(Restaurant model, IFormFile? logoFile)
        {
            var user = await _userManager.GetUserAsync(User);

            // 已經有店家了就不能再建
            var existing = await _db.Restaurants
                .FirstOrDefaultAsync(r => r.OwnerId == user!.Id);
            if (existing != null)
                return RedirectToAction("Index");

            var restaurant = new Restaurant
            {
                Name = model.Name,
                Description = model.Description,
                Address = model.Address,
                Phone = model.Phone,
                OwnerId = user!.Id
            };

            // 如果有上傳 Logo
            if (logoFile != null && logoFile.Length > 0)
            {
                try
                {
                    restaurant.LogoUrl = await _imageService.SaveImageAsync(logoFile, "restaurants");
                }
                catch (ArgumentException ex)
                {
                    ModelState.AddModelError(string.Empty, ex.Message);
                    return View(model);
                }
            }

            _db.Restaurants.Add(restaurant);
            await _db.SaveChangesAsync();

            TempData["Message"] = "店家建立成功！";
            return RedirectToAction("Index");
        }

        // ════════════════════════════════════════
        // 菜單管理
        // ════════════════════════════════════════

        // GET: /Owner/Menu
        public async Task<IActionResult> Menu()
        {
            var user = await _userManager.GetUserAsync(User);
            var restaurant = await _db.Restaurants
                .Include(r => r.MenuItems) // 一起把菜單載入
                .FirstOrDefaultAsync(r => r.OwnerId == user!.Id);

            if (restaurant == null)
                return RedirectToAction("SetupRestaurant");

            return View(restaurant.MenuItems.ToList());
        }

        // GET: /Owner/CreateMenuItem
        [HttpGet]
        public IActionResult CreateMenuItem()
        {
            return View(new MenuItemViewModel());
        }

        // POST: /Owner/CreateMenuItem
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(6_000_000)] // V-06 修復
        public async Task<IActionResult> CreateMenuItem(MenuItemViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            var user = await _userManager.GetUserAsync(User);
            var restaurant = await _db.Restaurants
                .FirstOrDefaultAsync(r => r.OwnerId == user!.Id);

            if (restaurant == null)
                return RedirectToAction("SetupRestaurant");

            var menuItem = new MenuItem
            {
                Name = model.Name,
                Description = model.Description,
                Price = model.Price,
                IsAvailable = model.IsAvailable,
                RestaurantId = restaurant.Id
            };

            // 處理圖片上傳
            if (model.ImageFile != null && model.ImageFile.Length > 0)
            {
                try
                {
                    menuItem.ImageUrl = await _imageService.SaveImageAsync(model.ImageFile, "menu");
                }
                catch (ArgumentException ex)
                {
                    ModelState.AddModelError(string.Empty, ex.Message);
                    return View(model);
                }
            }

            _db.MenuItems.Add(menuItem);
            await _db.SaveChangesAsync();

            TempData["Message"] = $"「{menuItem.Name}」新增成功！";
            return RedirectToAction("Menu");
        }

        // GET: /Owner/EditMenuItem/5
        [HttpGet]
        public async Task<IActionResult> EditMenuItem(int id)
        {
            var user = await _userManager.GetUserAsync(User);
            var restaurant = await _db.Restaurants
                .FirstOrDefaultAsync(r => r.OwnerId == user!.Id);

            // 確認這個餐點屬於這個老闆的店
            var menuItem = await _db.MenuItems
                .FirstOrDefaultAsync(m => m.Id == id && m.RestaurantId == restaurant!.Id);

            if (menuItem == null)
                return NotFound();

            var model = new MenuItemViewModel
            {
                Id = menuItem.Id,
                Name = menuItem.Name,
                Description = menuItem.Description,
                Price = menuItem.Price,
                IsAvailable = menuItem.IsAvailable,
                CurrentImageUrl = menuItem.ImageUrl // 顯示目前圖片
            };

            return View(model);
        }

        // POST: /Owner/EditMenuItem/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(6_000_000)] // V-06 修復
        public async Task<IActionResult> EditMenuItem(MenuItemViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            var user = await _userManager.GetUserAsync(User);
            var restaurant = await _db.Restaurants
                .FirstOrDefaultAsync(r => r.OwnerId == user!.Id);

            var menuItem = await _db.MenuItems
                .FirstOrDefaultAsync(m => m.Id == model.Id && m.RestaurantId == restaurant!.Id);

            if (menuItem == null)
                return NotFound();

            menuItem.Name = model.Name;
            menuItem.Description = model.Description;
            menuItem.Price = model.Price;
            menuItem.IsAvailable = model.IsAvailable;

            // 如果有上傳新圖片：先驗證新圖片存得起來，成功了才刪舊圖片，避免驗證失敗時新舊圖片一起消失
            if (model.ImageFile != null && model.ImageFile.Length > 0)
            {
                string newImageUrl;
                try
                {
                    newImageUrl = await _imageService.SaveImageAsync(model.ImageFile, "menu");
                }
                catch (ArgumentException ex)
                {
                    ModelState.AddModelError(string.Empty, ex.Message);
                    return View(model);
                }

                _imageService.DeleteImage(menuItem.ImageUrl);
                menuItem.ImageUrl = newImageUrl;
            }

            await _db.SaveChangesAsync();

            TempData["Message"] = $"「{menuItem.Name}」更新成功！";
            return RedirectToAction("Menu");
        }

        // POST: /Owner/DeleteMenuItem/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteMenuItem(int id)
        {
            var user = await _userManager.GetUserAsync(User);
            var restaurant = await _db.Restaurants
                .FirstOrDefaultAsync(r => r.OwnerId == user!.Id);

            var menuItem = await _db.MenuItems
                .FirstOrDefaultAsync(m => m.Id == id && m.RestaurantId == restaurant!.Id);

            if (menuItem == null)
                return NotFound();

            // V-02 修復：改用軟刪除，不再實體刪除資料列或圖片檔案。
            // 曾經被點過的餐點如果真的從資料庫刪掉，SQL Server 會連鎖刪光所有引用它的 OrderItem，
            // 歷史訂單金額就對不起來了；圖片檔案也一樣，實體刪除後無法復原。
            menuItem.IsDeleted = true;
            menuItem.IsAvailable = false;
            await _db.SaveChangesAsync();

            TempData["Message"] = $"「{menuItem.Name}」已下架。";
            return RedirectToAction("Menu");
        }

        // ════════════════════════════════════════
        // 訂單管理
        // ════════════════════════════════════════

        // GET: /Owner/Orders
        public async Task<IActionResult> Orders()
        {
            var user = await _userManager.GetUserAsync(User);
            var restaurant = await _db.Restaurants
                .FirstOrDefaultAsync(r => r.OwnerId == user!.Id);

            if (restaurant == null)
                return RedirectToAction("SetupRestaurant");

            var orders = await _db.Orders
                .Where(o => o.RestaurantId == restaurant.Id)
                .Include(o => o.Customer)
                .Include(o => o.OrderItems)
                    .ThenInclude(oi => oi.MenuItem) // 載入訂單明細的餐點資料
                .Include(o => o.Delivery)
                    .ThenInclude(d => d!.Driver)
                .OrderByDescending(o => o.CreatedAt)
                .ToListAsync();

            return View(orders);
        }

        // POST: /Owner/UpdateOrderStatus
        // 老闆更新訂單狀態（例如：已付款 → 備餐中）
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateOrderStatus(int orderId, OrderStatus newStatus)
        {
            // V-05 修復：C# 的 enum 參數不做值域驗證，傳 newStatus=99 這種不存在的狀態
            // 一樣會被模型繫結接受，Enum.IsDefined 先擋掉這種輸入。
            if (!Enum.IsDefined(typeof(OrderStatus), newStatus))
                return BadRequest();

            var user = await _userManager.GetUserAsync(User);
            var restaurant = await _db.Restaurants
                .FirstOrDefaultAsync(r => r.OwnerId == user!.Id);

            var order = await _db.Orders
                .Include(o => o.Customer)
                .FirstOrDefaultAsync(o => o.Id == orderId && o.RestaurantId == restaurant!.Id);

            if (order == null)
                return NotFound();

            // V-05 修復：只允許白名單內的狀態轉換，老闆不能把「待付款」直接改成「完成」繞過付款，
            // 也不能把「已送達」改回「待付款」。
            if (!OrderStatusTransitions.CanOwnerTransition(order.Status, newStatus))
            {
                TempData["Error"] = $"無法把訂單從「{order.Status.GetDisplayName()}」改成「{newStatus.GetDisplayName()}」。";
                return RedirectToAction("Orders");
            }

            order.Status = newStatus;

            // 訂單狀態一定要先存檔成功，寄信失敗不該連帶讓狀態更新消失
            // （原本存檔放在寄信迴圈之後：如果第 50 封信拋例外，SaveChangesAsync 永遠不會執行，
            //  狀態更新整個丟失，但前 49 位外送師已經收到「有新單」的通知——這是 P-05 的一部分）
            await _db.SaveChangesAsync();

            // 如果狀態改成「待取餐」，寄 Email 通知外送師
            if (newStatus == OrderStatus.ReadyForPickup)
            {
                // 找有在線的外送師（簡單版：寄給所有外送師）
                var drivers = await _userManager.Users
                    .Where(u => u.Role == UserRole.Driver)
                    .ToListAsync();

                // V-13 修復：連結不再硬編 https://localhost:7001，改用 Url.Action 依目前這個請求
                // 實際的網域產生，換到雲端環境部署也不用改程式碼。
                var pickupUrl = Url.Action("AvailableOrders", "Driver", null, Request.Scheme);

                // V-13 修復：DeliveryAddress 是顧客自由輸入的欄位，直接內插進 HTML 信件
                // 等於允許顧客把姓名/地址設成 <a href="...">釣魚連結</a>，用平台自己的網域寄出去。
                var safeAddress = System.Net.WebUtility.HtmlEncode(order.DeliveryAddress);

                foreach (var driver in drivers)
                {
                    await _emailService.SendEmailAsync(
                        driver.Email!,
                        "【YUYUEAT】新訂單可以接單！",
                        $@"<h2>有新訂單可以接！</h2>
                           <p>訂單編號：<strong>{order.OrderNumber}</strong></p>
                           <p>金額：<strong>${order.GrandTotal}</strong></p>
                           <p>外送地址：{safeAddress}</p>
                           <p>請登入系統接單：
                              <a href='{pickupUrl}'>
                                 點我接單
                              </a>
                           </p>"
                    );
                }
            }

            TempData["Message"] = $"訂單 {order.OrderNumber} 狀態已更新！";
            return RedirectToAction("Orders");
        }
    }
}