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
        private readonly IEmailQueue _emailQueue;
        private readonly IOrderService _orderService;

        public OwnerController(
            AppDbContext db,
            UserManager<ApplicationUser> userManager,
            IImageService imageService,
            IEmailQueue emailQueue,
            IOrderService orderService)
        {
            _db = db;
            _userManager = userManager;
            _imageService = imageService;
            _emailQueue = emailQueue;
            _orderService = orderService;
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

            // 取得最新10筆訂單（給畫面的「最近訂單」清單用）
            var recentOrders = await _db.Orders
                .Where(o => o.RestaurantId == restaurant.Id)
                .Include(o => o.Customer)   // Include = 同時載入關聯資料
                .Include(o => o.OrderItems)
                .OrderByDescending(o => o.CreatedAt)
                .Take(10)
                .ToListAsync();

            ViewBag.Restaurant = restaurant;
            ViewBag.RecentOrders = recentOrders;

            // P-01 修復：統計數字原本是對上面那 10 筆「最近訂單」算的，
            // 今天真的有 30 張單，後台會顯示「今日訂單：10」。改成對整個資料集分別下
            // CountAsync()，SQL Server 算 COUNT(*)，不用把資料撈進記憶體。
            var today = DateTime.Today;
            ViewBag.TodayOrderCount = await _db.Orders.CountAsync(o =>
                o.RestaurantId == restaurant.Id && o.CreatedAt >= today && o.CreatedAt < today.AddDays(1));
            ViewBag.PendingOrderCount = await _db.Orders.CountAsync(o =>
                o.RestaurantId == restaurant.Id && o.Status == OrderStatus.Paid);

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
        public async Task<IActionResult> Orders(int page = 1)
        {
            var user = await _userManager.GetUserAsync(User);
            var restaurant = await _db.Restaurants
                .FirstOrDefaultAsync(r => r.OwnerId == user!.Id);

            if (restaurant == null)
                return RedirectToAction("SetupRestaurant");

            // M3 修復（§3.2 全站零分頁 / §3.3 唯讀查詢加 AsNoTracking）：
            // 這個查詢只是顯示訂單列表，狀態變更走 UpdateOrderStatus 另外重查，
            // 不會用到這裡載入的追蹤實體，AsNoTracking() 是安全的。
            var orders = await _db.Orders
                .Where(o => o.RestaurantId == restaurant.Id)
                .Include(o => o.Customer)
                .Include(o => o.OrderItems)
                    .ThenInclude(oi => oi.MenuItem) // 載入訂單明細的餐點資料
                .Include(o => o.Delivery)
                    .ThenInclude(d => d!.Driver)
                .OrderByDescending(o => o.CreatedAt)
                .AsNoTracking()
                .ToPagedResultAsync(page);

            return View(orders);
        }

        // POST: /Owner/UpdateOrderStatus
        // 老闆更新訂單狀態（例如：已付款 → 備餐中）
        // M3 修復（§3.1 Service 層拆分）：狀態轉換白名單驗證與存檔搬進 IOrderService，
        // 這裡只負責取得結果、決定要不要寄通知信。
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateOrderStatus(int orderId, OrderStatus newStatus)
        {
            var user = await _userManager.GetUserAsync(User);
            var result = await _orderService.UpdateOwnerStatusAsync(orderId, user!.Id, newStatus);

            if (!result.Success)
            {
                if (result.FailureReason == StatusUpdateFailureReason.NotFound)
                    return NotFound();

                TempData["Error"] = result.ErrorMessage;
                return RedirectToAction("Orders");
            }

            var order = result.Order!;

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

                // M3 修復（P-05）：原本在這個 foreach 裡逐一同步呼叫 SMTP，100 個外送師
                // 就是 100 次同步連線，老闆按一個按鈕要等好幾分鐘，HTTP 請求全程被卡住。
                // 現在只是把訊息丟進記憶體佇列，這個迴圈幾乎是瞬間跑完。
                foreach (var driver in drivers)
                {
                    await _emailQueue.EnqueueAsync(new EmailMessage(
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
                    ));
                }
            }

            TempData["Message"] = $"訂單 {order.OrderNumber} 狀態已更新！";
            return RedirectToAction("Orders");
        }
    }
}