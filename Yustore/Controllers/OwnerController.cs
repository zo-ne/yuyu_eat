using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Yustore.Data;
using Yustore.Enums;
using Yustore.Filters;
using Yustore.Models.Entities;
using Yustore.Services;
using Yustore.ViewModels;

namespace Yustore.Controllers
{
    // [OwnerOnly] 套用在整個 Controller
    // 表示這裡所有的 Action 都只有老闆能進入
    [OwnerOnly]
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
                .Count(o => o.Status == OrderStatus.已付款);

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
                restaurant.LogoUrl = await _imageService.SaveImageAsync(logoFile, "restaurants");

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
                menuItem.ImageUrl = await _imageService.SaveImageAsync(model.ImageFile, "menu");

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

            // 如果有上傳新圖片
            if (model.ImageFile != null && model.ImageFile.Length > 0)
            {
                // 先刪舊圖片
                _imageService.DeleteImage(menuItem.ImageUrl);
                // 存新圖片
                menuItem.ImageUrl = await _imageService.SaveImageAsync(model.ImageFile, "menu");
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

            // 刪圖片檔案
            _imageService.DeleteImage(menuItem.ImageUrl);

            _db.MenuItems.Remove(menuItem);
            await _db.SaveChangesAsync();

            TempData["Message"] = $"「{menuItem.Name}」已刪除。";
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
            var user = await _userManager.GetUserAsync(User);
            var restaurant = await _db.Restaurants
                .FirstOrDefaultAsync(r => r.OwnerId == user!.Id);

            var order = await _db.Orders
                .Include(o => o.Customer)
                .FirstOrDefaultAsync(o => o.Id == orderId && o.RestaurantId == restaurant!.Id);

            if (order == null)
                return NotFound();

            order.Status = newStatus;

            // 如果狀態改成「待取餐」，寄 Email 通知外送師
            if (newStatus == OrderStatus.待取餐)
            {
                // 找有在線的外送師（簡單版：寄給所有外送師）
                var drivers = await _userManager.Users
                    .Where(u => u.Role == UserRole.外送師)
                    .ToListAsync();

                foreach (var driver in drivers)
                {
                    await _emailService.SendEmailAsync(
                        driver.Email!,
                        "【YUYUEAT】新訂單可以接單！",
                        $@"<h2>有新訂單可以接！</h2>
                           <p>訂單編號：<strong>{order.OrderNumber}</strong></p>
                           <p>金額：<strong>${order.GrandTotal}</strong></p>
                           <p>外送地址：{order.DeliveryAddress}</p>
                           <p>請登入系統接單：
                              <a href='https://localhost:7001/Driver/AvailableOrders'>
                                 點我接單
                              </a>
                           </p>"
                    );
                }
            }

            await _db.SaveChangesAsync();

            TempData["Message"] = $"訂單 {order.OrderNumber} 狀態已更新！";
            return RedirectToAction("Orders");
        }
    }
}