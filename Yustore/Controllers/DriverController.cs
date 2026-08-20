using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Yustore.Data;
using Yustore.Enums;
using Yustore.Extensions;
using Yustore.Filters;
using Yustore.Models.Entities;
using Yustore.Services;

namespace Yustore.Controllers
{
    [RoleRequired(UserRole.Driver)]
    public class DriverController : Controller
    {
        private readonly AppDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IImageService _imageService;
        private readonly ISettlementService _settlementService;

        public DriverController(
            AppDbContext db,
            UserManager<ApplicationUser> userManager,
            IImageService imageService,
            ISettlementService settlementService)
        {
            _db = db;
            _userManager = userManager;
            _imageService = imageService;
            _settlementService = settlementService;
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
        public async Task<IActionResult> AvailableOrders(int page = 1)
        {
            // 找出所有「待取餐」且還沒有外送師接的訂單
            // （M3 修復：加分頁 §3.2；AsNoTracking §3.3——AcceptOrder 用 ExecuteUpdateAsync
            //   走另一條原子更新路徑，不依賴這裡載入的追蹤實體）
            var orders = await _db.Orders
                .Where(o => o.Status == OrderStatus.ReadyForPickup
                    && o.Delivery == null) // 還沒有人接
                .Include(o => o.Restaurant)
                .Include(o => o.Customer)
                .Include(o => o.OrderItems)
                .OrderBy(o => o.CreatedAt) // 先進先出
                .AsNoTracking()
                .ToPagedResultAsync(page);

            return View(orders);
        }

        // POST: /Driver/AcceptOrder
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AcceptOrder(int orderId)
        {
            var user = await _userManager.GetUserAsync(User);

            // V-11 修復：原本「查詢」跟「寫入」是兩個分開的步驟，中間有時間差 ——
            // 兩位外送師同時按接單，兩邊的查詢都會通過檢查，其中一邊寫入時才會因為
            // Delivery.OrderId 的 unique 索引而失敗，而且是丟出未處理例外變成 500 錯誤頁。
            // 改成條件式 UPDATE（WHERE Status = ReadyForPickup AND Delivery IS NULL），
            // 這是資料庫層級的單一原子操作，兩個並發請求只會有一個真的更新成功。
            var claimed = await _db.Orders
                .Where(o => o.Id == orderId && o.Status == OrderStatus.ReadyForPickup && o.Delivery == null)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, OrderStatus.OutForDelivery));

            if (claimed == 0)
            {
                TempData["Error"] = "這筆訂單已被其他外送師接走了！";
                return RedirectToAction("AvailableOrders");
            }

            // 建立 Delivery 記錄。前面的原子 UPDATE 已經確保只有我們搶到這筆訂單，
            // 但還是包 try/catch，讓 unique 索引這道最後防線萬一擋下來時顯示友善訊息而不是 500。
            var delivery = new Delivery
            {
                OrderId = orderId,
                DriverId = user!.Id,
                PickedUpAt = DateTime.Now
            };

            _db.Deliveries.Add(delivery);

            try
            {
                await _db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                TempData["Error"] = "接單失敗，請重新整理後再試一次。";
                return RedirectToAction("AvailableOrders");
            }

            TempData["Message"] = "接單成功！請前往取餐。";
            return RedirectToAction("MyOrders");
        }

        // ════════════════════════════════════════
        // 我的訂單（進行中）
        // ════════════════════════════════════════

        public async Task<IActionResult> MyOrders(int page = 1)
        {
            var user = await _userManager.GetUserAsync(User);

            // M3 修復（§3.2 全站零分頁 / §3.3 唯讀查詢加 AsNoTracking）
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
                .AsNoTracking()
                .ToPagedResultAsync(page);

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
        [RequestSizeLimit(6_000_000)] // V-06 修復
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
                try
                {
                    delivery.ProofPhotoUrl = await _imageService
                        .SaveImageAsync(proofPhoto, "proofs");
                }
                catch (ArgumentException ex)
                {
                    TempData["Error"] = ex.Message;
                    return RedirectToAction("CompleteOrder", new { deliveryId });
                }
            }

            // 記錄送達時間
            delivery.DeliveredAt = DateTime.Now;

            // 更新訂單狀態為「已送達」
            delivery.Order.Status = OrderStatus.Delivered;

            // 建立結算記錄（M3 修復 §3.1：搬進 ISettlementService）。
            // 這裡刻意不先呼叫 SaveChangesAsync：DriverController 跟 SettlementService
            // 共用同一個（Scoped）DbContext，delivery/order 的變更已經在追蹤中，
            // CreateForDeliveryAsync 內部的 SaveChangesAsync 會把兩邊的異動一起存檔，
            // 維持「送達狀態」跟「結算記錄」這兩件事原子性地同時成功或同時失敗。
            // 外送師收了顧客的現金，餐費部分要月底結算給老闆
            await _settlementService.CreateForDeliveryAsync(
                delivery.Order.Id, delivery.Order.RestaurantId, delivery.Order.FoodTotal, user!.Id);

            TempData["Message"] = "✅ 訂單完成！結算記錄已建立。";
            return RedirectToAction("MyOrders");
        }
    }
}