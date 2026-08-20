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
    [RoleRequired(UserRole.Customer)]
    public class CustomerController : Controller
    {
        private readonly AppDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ICartService _cartService;
        private readonly IOrderService _orderService;
        private readonly IRestaurantService _restaurantService;

        public CustomerController(
            AppDbContext db,
            UserManager<ApplicationUser> userManager,
            ICartService cartService,
            IOrderService orderService,
            IRestaurantService restaurantService)
        {
            _db = db;
            _userManager = userManager;
            _cartService = cartService;
            _orderService = orderService;
            _restaurantService = restaurantService;
        }

        // ════════════════════════════════════════
        // 首頁：瀏覽店家
        // ════════════════════════════════════════

        public async Task<IActionResult> Index(string? search, int page = 1)
        {
            var restaurants = await _restaurantService.SearchOpenRestaurantsAsync(search, page);

            ViewBag.Search = search;
            return View(restaurants);
        }

        // ════════════════════════════════════════
        // 店家頁面：查看菜單
        // ════════════════════════════════════════

        public async Task<IActionResult> Restaurant(int id)
        {
            var detail = await _restaurantService.GetDetailAsync(id);
            if (detail == null)
                return NotFound();

            ViewBag.ReviewCount = detail.ReviewCount;
            ViewBag.AverageRating = detail.AverageRating;

            // 取得目前購物車（顯示已選數量用）
            var cart = _cartService.GetCart(HttpContext);
            ViewBag.Cart = cart;

            return View(detail.Restaurant);
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
        // M3 修復（§3.1 Service 層拆分）：結帳的重新驗價/建單邏輯搬進 IOrderService，
        // 這裡只負責處理 HTTP 層的事（讀購物車、驗證表單、依結果決定要導去哪一頁、清購物車）。
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

            var user = await _userManager.GetUserAsync(User);
            var result = await _orderService.CheckoutAsync(user!.Id, cart, model.DeliveryAddress, model.Note);

            if (!result.Success)
            {
                TempData["Error"] = result.ErrorMessage;
                return RedirectToAction("Cart");
            }

            // 清空購物車
            _cartService.ClearCart(HttpContext);

            // 跳到模擬付款頁面
            return RedirectToAction("Payment", new { orderId = result.Order!.Id });
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
            var order = await _orderService.ConfirmPaymentAsync(orderId, user!.Id);

            if (order == null)
                return NotFound();

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