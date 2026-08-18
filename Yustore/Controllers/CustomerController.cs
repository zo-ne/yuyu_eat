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
    [RoleRequired(UserRole.Customer)]
    public class CustomerController : Controller
    {
        private readonly AppDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ICartService _cartService;

        public CustomerController(
            AppDbContext db,
            UserManager<ApplicationUser> userManager,
            ICartService cartService)
        {
            _db = db;
            _userManager = userManager;
            _cartService = cartService;
        }

        // ════════════════════════════════════════
        // 首頁：瀏覽店家
        // ════════════════════════════════════════

        public async Task<IActionResult> Index(string? search)
        {
            // IQueryable = 還沒真正執行查詢，可以繼續加條件
            var query = _db.Restaurants
                .Where(r => r.IsOpen)
                .AsQueryable();

            // 如果有搜尋關鍵字
            if (!string.IsNullOrEmpty(search))
            {
                query = query.Where(r =>
                    r.Name.Contains(search) ||
                    r.Description!.Contains(search));
            }

            var restaurants = await query
                .Include(r => r.Owner)
                .ToListAsync();

            ViewBag.Search = search;
            return View(restaurants);
        }

        // ════════════════════════════════════════
        // 店家頁面：查看菜單
        // ════════════════════════════════════════

        public async Task<IActionResult> Restaurant(int id)
        {
            var restaurant = await _db.Restaurants
                .Include(r => r.MenuItems.Where(m => m.IsAvailable)) // 只載入供應中的餐點
                .Include(r => r.Owner)
                .FirstOrDefaultAsync(r => r.Id == id && r.IsOpen);

            if (restaurant == null)
                return NotFound();

            // 取得這家店的評分
            var reviews = await _db.Reviews
                .Where(r => r.TargetUserId == restaurant.OwnerId)
                .ToListAsync();

            ViewBag.AverageRating = reviews.Any()
                ? Math.Round(reviews.Average(r => r.Stars), 1)
                : 0;
            ViewBag.ReviewCount = reviews.Count;

            // 取得目前購物車（顯示已選數量用）
            var cart = _cartService.GetCart(HttpContext);
            ViewBag.Cart = cart;

            return View(restaurant);
        }

        // ════════════════════════════════════════
        // 購物車
        // ════════════════════════════════════════

        // POST: 加入購物車
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddToCart(int menuItemId, int quantity = 1)
        {
            var menuItem = await _db.MenuItems
                .Include(m => m.Restaurant)
                .FirstOrDefaultAsync(m => m.Id == menuItemId && m.IsAvailable);

            if (menuItem == null)
                return NotFound();

            // V-03 修復：原本完全不驗證數量範圍，送 quantity=-1000 可以做出負數訂單總額，
            // quantity=999999999 則會讓 decimal 欄位溢位而 500。
            if (quantity < 1 || quantity > 99)
            {
                TempData["Error"] = "數量必須介於 1 到 99 之間。";
                return RedirectToAction("Restaurant", new { id = menuItem.RestaurantId });
            }

            var cartItem = new CartItemViewModel
            {
                MenuItemId = menuItem.Id,
                Name = menuItem.Name,
                Price = menuItem.Price,
                Quantity = quantity,
                ImageUrl = menuItem.ImageUrl
            };

            _cartService.AddToCart(HttpContext, cartItem,
                menuItem.RestaurantId, menuItem.Restaurant.Name);

            TempData["Message"] = $"「{menuItem.Name}」已加入購物車！";
            return RedirectToAction("Restaurant", new { id = menuItem.RestaurantId });
        }

        // GET: 查看購物車
        public IActionResult Cart()
        {
            var cart = _cartService.GetCart(HttpContext);
            return View(cart);
        }

        // POST: 更新數量
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult UpdateCart(int menuItemId, int quantity)
        {
            _cartService.UpdateQuantity(HttpContext, menuItemId, quantity);
            return RedirectToAction("Cart");
        }

        // POST: 移除項目
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult RemoveFromCart(int menuItemId)
        {
            _cartService.RemoveItem(HttpContext, menuItemId);
            return RedirectToAction("Cart");
        }

        // ════════════════════════════════════════
        // 結帳
        // ════════════════════════════════════════

        // GET: 結帳頁面
        public IActionResult Checkout()
        {
            var cart = _cartService.GetCart(HttpContext);

            if (!cart.Items.Any())
            {
                TempData["Error"] = "購物車是空的！";
                return RedirectToAction("Cart");
            }

            var model = new CheckoutViewModel { Cart = cart };
            return View(model);
        }

        // POST: 確認下單
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Checkout(CheckoutViewModel model)
        {
            var cart = _cartService.GetCart(HttpContext);

            if (!cart.Items.Any())
                return RedirectToAction("Index");

            // 把 Cart 塞回 model（因為 POST 後 Cart 會是 null）
            model.Cart = cart;

            if (!ModelState.IsValid)
                return View(model);

            // V-04 修復：購物車裡的 Price 是「加入購物車當下」的快照，Session 有 30 分鐘壽命，
            // 這段期間老闆可能漲價、下架、甚至刪除餐點。結帳這一刻要重新查資料庫，
            // 用資料庫目前的價格與供應狀態為準，不能相信 Session 裡的舊資料。
            var menuItemIds = cart.Items.Select(i => i.MenuItemId).ToList();
            var freshMenuItems = await _db.MenuItems
                .Where(m => menuItemIds.Contains(m.Id))
                .ToDictionaryAsync(m => m.Id);

            var invalidItems = cart.Items.Where(item =>
                !freshMenuItems.TryGetValue(item.MenuItemId, out var menuItem) ||
                !menuItem.IsAvailable ||
                menuItem.RestaurantId != cart.RestaurantId).ToList();

            if (invalidItems.Any())
            {
                TempData["Error"] = "購物車裡有餐點已經下架、售完或不屬於這家店，請重新確認後再結帳。";
                return RedirectToAction("Cart");
            }

            var user = await _userManager.GetUserAsync(User);

            // 產生訂單編號：ORD-日期 + 資料庫自增序號，避免 Random 碰撞（V-10 修復）
            var orderNumber = await GenerateOrderNumberAsync();

            // 用資料庫目前的價格重新計算，不採用購物車裡的快照價格
            var orderItems = cart.Items.Select(item =>
            {
                var menuItem = freshMenuItems[item.MenuItemId];
                return new OrderItem
                {
                    MenuItemId = menuItem.Id,
                    MenuItemName = menuItem.Name, // 名稱快照，之後餐點被下架也不影響歷史訂單顯示（V-02 修復）
                    Quantity = item.Quantity,
                    UnitPrice = menuItem.Price,
                    Subtotal = menuItem.Price * item.Quantity
                };
            }).ToList();

            var foodTotal = orderItems.Sum(i => i.Subtotal);
            const decimal deliveryFee = 30; // 目前固定外送費，之後動態外送費上線時改成伺服器端計算

            var order = new Order
            {
                OrderNumber = orderNumber,
                Status = OrderStatus.PendingPayment,
                FoodTotal = foodTotal,
                DeliveryFee = deliveryFee,
                GrandTotal = foodTotal + deliveryFee,
                DeliveryAddress = model.DeliveryAddress,
                Note = model.Note,
                CustomerId = user!.Id,
                RestaurantId = cart.RestaurantId
            };

            foreach (var orderItem in orderItems)
                order.OrderItems.Add(orderItem);

            // 建單整段包一個 transaction；OrderNumber 有 unique 索引兜底，
            // 極端情況下（同一秒有其他訂單搶到同一個序號）寧可讓這次結帳失敗、請顧客重試，
            // 也不要讓兩張訂單共用同一個編號。
            using var transaction = await _db.Database.BeginTransactionAsync();
            try
            {
                _db.Orders.Add(order);
                await _db.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch (DbUpdateException)
            {
                await transaction.RollbackAsync();
                TempData["Error"] = "系統忙線中，訂單建立失敗，請重新結帳一次。";
                return RedirectToAction("Checkout");
            }

            // 清空購物車
            _cartService.ClearCart(HttpContext);

            // 跳到模擬付款頁面
            return RedirectToAction("Payment", new { orderId = order.Id });
        }

        // V-10 修復：原本用 $"ORD-{yyyyMMdd}-{new Random().Next(1000, 9999)}"，
        // 同一天只有 8999 種可能，約 112 筆訂單就有 50% 機率碰撞，且 DB 沒有 unique 約束不會報錯，
        // 只會安靜地產生兩張一樣編號的訂單。改成「當天序號」：查當天已有幾筆訂單，用下一號補零到 6 碼，
        // 搭配 Order.OrderNumber 的 unique 索引（見 AppDbContext），萬一真的撞號會直接讓交易失敗而不是悄悄重複。
        private async Task<string> GenerateOrderNumberAsync()
        {
            var today = DateTime.Now.Date;
            var todayCount = await _db.Orders.CountAsync(o => o.CreatedAt >= today && o.CreatedAt < today.AddDays(1));
            return $"ORD-{DateTime.Now:yyyyMMdd}-{(todayCount + 1):D6}";
        }

        // ════════════════════════════════════════
        // 模擬付款
        // ════════════════════════════════════════

        // GET: 付款頁面
        public async Task<IActionResult> Payment(int orderId)
        {
            var user = await _userManager.GetUserAsync(User);
            var order = await _db.Orders
                .Include(o => o.OrderItems)
                    .ThenInclude(oi => oi.MenuItem)
                .FirstOrDefaultAsync(o => o.Id == orderId && o.CustomerId == user!.Id);

            if (order == null)
                return NotFound();

            // 只有「待付款」的訂單才能進付款頁面
            if (order.Status != OrderStatus.PendingPayment)
                return RedirectToAction("OrderDetail", new { orderId = order.Id });

            return View(order);
        }

        // POST: 確認付款（模擬）
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ConfirmPayment(int orderId)
        {
            var user = await _userManager.GetUserAsync(User);
            var order = await _db.Orders
                .FirstOrDefaultAsync(o => o.Id == orderId && o.CustomerId == user!.Id);

            if (order == null)
                return NotFound();

            // 模擬付款成功：狀態改為「已付款」
            order.Status = OrderStatus.Paid;
            await _db.SaveChangesAsync();

            TempData["Message"] = $"✅ 付款成功！訂單編號：{order.OrderNumber}";
            return RedirectToAction("OrderDetail", new { orderId = order.Id });
        }

        // ════════════════════════════════════════
        // 訂單查詢
        // ════════════════════════════════════════

        // GET: 我的訂單列表
        public async Task<IActionResult> MyOrders()
        {
            var user = await _userManager.GetUserAsync(User);
            var orders = await _db.Orders
                .Where(o => o.CustomerId == user!.Id)
                .Include(o => o.Restaurant)
                .Include(o => o.OrderItems)
                .OrderByDescending(o => o.CreatedAt)
                .ToListAsync();

            return View(orders);
        }

        // GET: 訂單詳細
        public async Task<IActionResult> OrderDetail(int orderId)
        {
            var user = await _userManager.GetUserAsync(User);
            var order = await _db.Orders
                .Include(o => o.Restaurant)
                .Include(o => o.OrderItems)
                    .ThenInclude(oi => oi.MenuItem)
                .Include(o => o.Delivery)
                    .ThenInclude(d => d!.Driver)
                .FirstOrDefaultAsync(o => o.Id == orderId && o.CustomerId == user!.Id);

            if (order == null)
                return NotFound();

            return View(order);
        }
    }
}