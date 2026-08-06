using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Yustore.Data;
using Yustore.Enums;
using Yustore.Filters;
using Yustore.Models.Entities;
using Yustore.Services;

namespace Yustore.Controllers
{
    [DriverOnly]
    public class DriverController : Controller
    {
        private readonly AppDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IImageService _imageService;

        public DriverController(
            AppDbContext db,
            UserManager<ApplicationUser> userManager,
            IImageService imageService)
        {
            _db = db;
            _userManager = userManager;
            _imageService = imageService;
        }

        // ════════════════════════════════════════
        // 首頁：目前接單狀態
        // ════════════════════════════════════════

        public async Task<IActionResult> Index()
        {
            var user = await _userManager.GetUserAsync(User);

            // 取得我目前進行中的訂單
            var activeDeliveries = await _db.Deliveries
                .Where(d => d.DriverId == user!.Id && d.DeliveredAt == null)
                .Include(d => d.Order)
                    .ThenInclude(o => o.Restaurant)
                .Include(d => d.Order)
                    .ThenInclude(o => o.Customer)
                .Include(d => d.Order)
                    .ThenInclude(o => o.OrderItems)
                .ToListAsync();

            // 本月完成訂單數
            var monthlyCount = await _db.Deliveries
                .Where(d => d.DriverId == user!.Id
                    && d.DeliveredAt != null
                    && d.DeliveredAt.Value.Month == DateTime.Now.Month
                    && d.DeliveredAt.Value.Year == DateTime.Now.Year)
                .CountAsync();

            // 本月外送費收入（每單 $30）
            ViewBag.MonthlyIncome = monthlyCount * 30;
            ViewBag.MonthlyCount = monthlyCount;
            ViewBag.ActiveDeliveries = activeDeliveries;

            return View();
        }

        // ════════════════════════════════════════
        // 可接訂單列表
        // ════════════════════════════════════════

        // GET: /Driver/AvailableOrders
        public async Task<IActionResult> AvailableOrders()
        {
            // 找出所有「待取餐」且還沒有外送師接的訂單
            var orders = await _db.Orders
                .Where(o => o.Status == OrderStatus.待取餐
                    && o.Delivery == null) // 還沒有人接
                .Include(o => o.Restaurant)
                .Include(o => o.Customer)
                .Include(o => o.OrderItems)
                .OrderBy(o => o.CreatedAt) // 先進先出
                .ToListAsync();

            return View(orders);
        }

        // POST: /Driver/AcceptOrder
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AcceptOrder(int orderId)
        {
            var user = await _userManager.GetUserAsync(User);

            // 確認訂單存在且是「待取餐」且還沒人接
            var order = await _db.Orders
                .Include(o => o.Delivery)
                .FirstOrDefaultAsync(o => o.Id == orderId
                    && o.Status == OrderStatus.待取餐
                    && o.Delivery == null);

            if (order == null)
            {
                TempData["Error"] = "這筆訂單已被其他外送師接走了！";
                return RedirectToAction("AvailableOrders");
            }

            // 建立 Delivery 記錄
            var delivery = new Delivery
            {
                OrderId = orderId,
                DriverId = user!.Id,
                PickedUpAt = DateTime.Now
            };

            // 更新訂單狀態為「外送中」
            order.Status = OrderStatus.外送中;

            _db.Deliveries.Add(delivery);
            await _db.SaveChangesAsync();

            TempData["Message"] = "接單成功！請前往取餐。";
            return RedirectToAction("MyOrders");
        }

        // ════════════════════════════════════════
        // 我的訂單（進行中）
        // ════════════════════════════════════════

        public async Task<IActionResult> MyOrders()
        {
            var user = await _userManager.GetUserAsync(User);

            var deliveries = await _db.Deliveries
                .Where(d => d.DriverId == user!.Id)
                .Include(d => d.Order)
                    .ThenInclude(o => o.Restaurant)
                .Include(d => d.Order)
                    .ThenInclude(o => o.Customer)
                .Include(d => d.Order)
                    .ThenInclude(o => o.OrderItems)
                        .ThenInclude(oi => oi.MenuItem)
                .OrderByDescending(d => d.Order.CreatedAt)
                .ToListAsync();

            return View(deliveries);
        }

        // ════════════════════════════════════════
        // 完成訂單（拍照上傳）
        // ════════════════════════════════════════

        // GET: /Driver/CompleteOrder/5
        [HttpGet]
        public async Task<IActionResult> CompleteOrder(int deliveryId)
        {
            var user = await _userManager.GetUserAsync(User);

            var delivery = await _db.Deliveries
                .Include(d => d.Order)
                    .ThenInclude(o => o.Customer)
                .Include(d => d.Order)
                    .ThenInclude(o => o.Restaurant)
                .FirstOrDefaultAsync(d => d.Id == deliveryId
                    && d.DriverId == user!.Id
                    && d.DeliveredAt == null); // 還沒完成的

            if (delivery == null)
                return NotFound();

            return View(delivery);
        }

        // POST: /Driver/CompleteOrder
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CompleteOrder(int deliveryId, IFormFile? proofPhoto)
        {
            var user = await _userManager.GetUserAsync(User);

            var delivery = await _db.Deliveries
                .Include(d => d.Order)
                .FirstOrDefaultAsync(d => d.Id == deliveryId
                    && d.DriverId == user!.Id
                    && d.DeliveredAt == null);

            if (delivery == null)
                return NotFound();

            // 上傳完成照片
            if (proofPhoto != null && proofPhoto.Length > 0)
            {
                delivery.ProofPhotoUrl = await _imageService
                    .SaveImageAsync(proofPhoto, "proofs");
            }

            // 記錄送達時間
            delivery.DeliveredAt = DateTime.Now;

            // 更新訂單狀態為「已送達」
            delivery.Order.Status = OrderStatus.已送達;

            // 建立結算記錄
            // 外送師收了顧客的現金，餐費部分要月底結算給老闆
            var restaurant = await _db.Restaurants
                .FirstOrDefaultAsync(r => r.Id == delivery.Order.RestaurantId);

            var settlement = new Settlement
            {
                OrderId = delivery.Order.Id,
                DriverId = user!.Id,
                OwnerId = restaurant!.OwnerId,
                FoodAmount = delivery.Order.FoodTotal, // 餐費（不含外送費）
                Status = SettlementStatus.未結算,
                Year = DateTime.Now.Year,
                Month = DateTime.Now.Month
            };

            _db.Settlements.Add(settlement);
            await _db.SaveChangesAsync();

            TempData["Message"] = "✅ 訂單完成！結算記錄已建立。";
            return RedirectToAction("MyOrders");
        }
    }
}