using Microsoft.AspNetCore.Mvc;
using Yustore.Enums;
using Yustore.Filters;
using Yustore.Services;

namespace Yustore.Controllers
{
    // M4 新增（docs/PRD-v2.md §5.6）：Admin 治理後台——審核佇列、停權管理、
    // 全平台訂單總覽、結算批次管理。Admin 帳號只能透過資料庫 Seed 建立
    // （見 Program.cs），沒有自助註冊入口。
    [RoleRequired(UserRole.Admin)]
    public class AdminController : Controller
    {
        private readonly IAdminService _adminService;
        private readonly ISettlementService _settlementService;

        public AdminController(IAdminService adminService, ISettlementService settlementService)
        {
            _adminService = adminService;
            _settlementService = settlementService;
        }

        // ════════════════════════════════════════
        // 首頁
        // ════════════════════════════════════════

        public async Task<IActionResult> Index()
        {
            var pending = await _adminService.GetPendingApplicationsAsync(page: 1);
            ViewBag.PendingCount = pending.TotalCount;
            return View();
        }

        // ════════════════════════════════════════
        // 審核佇列
        // ════════════════════════════════════════

        // GET: /Admin/Applications
        public async Task<IActionResult> Applications(int page = 1)
        {
            var applications = await _adminService.GetPendingApplicationsAsync(page);
            return View(applications);
        }

        // POST: /Admin/ApproveApplication
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ApproveApplication(string userId)
        {
            var result = await _adminService.ApproveApplicationAsync(userId);

            if (!result.Success)
            {
                if (result.Result == AdminOpResultKind.NotFound)
                    return NotFound();

                TempData["Error"] = result.ErrorMessage;
                return RedirectToAction("Applications");
            }

            TempData["Message"] = "已核准申請。";
            return RedirectToAction("Applications");
        }

        // POST: /Admin/RejectApplication
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RejectApplication(string userId, string reason)
        {
            var result = await _adminService.RejectApplicationAsync(userId, reason);

            if (!result.Success)
            {
                if (result.Result == AdminOpResultKind.NotFound)
                    return NotFound();

                TempData["Error"] = result.ErrorMessage;
                return RedirectToAction("Applications");
            }

            TempData["Message"] = "已退回申請。";
            return RedirectToAction("Applications");
        }

        // ════════════════════════════════════════
        // 使用者管理（停權）
        // ════════════════════════════════════════

        // GET: /Admin/Users
        public async Task<IActionResult> Users(UserRole? role, int page = 1)
        {
            var users = await _adminService.GetUsersAsync(role, page);
            ViewBag.RoleFilter = role;
            return View(users);
        }

        // POST: /Admin/SetActive
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SetActive(string userId, bool isActive, UserRole? role)
        {
            var result = await _adminService.SetActiveAsync(userId, isActive);

            if (!result.Success)
            {
                if (result.Result == AdminOpResultKind.NotFound)
                    return NotFound();

                TempData["Error"] = result.ErrorMessage;
            }
            else
            {
                TempData["Message"] = isActive ? "帳號已恢復啟用。" : "帳號已停權。";
            }

            return RedirectToAction("Users", new { role });
        }

        // ════════════════════════════════════════
        // 全平台訂單總覽
        // ════════════════════════════════════════

        // GET: /Admin/Orders
        public async Task<IActionResult> Orders(
            OrderStatus? status, DateTime? from, DateTime? to, int? restaurantId, int page = 1)
        {
            var orders = await _adminService.GetOrdersAsync(status, from, to, restaurantId, page);

            ViewBag.Status = status;
            ViewBag.From = from;
            ViewBag.To = to;
            ViewBag.RestaurantId = restaurantId;

            return View(orders);
        }

        // ════════════════════════════════════════
        // 結算批次管理（沿用 ISettlementService，見 docs/PRD-v2.md §4）
        // ════════════════════════════════════════

        // GET: /Admin/Settlements
        public async Task<IActionResult> Settlements(int page = 1)
        {
            var batches = await _settlementService.GetBatchesAsync(page);
            return View(batches);
        }

        // POST: /Admin/GenerateSettlementBatches
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GenerateSettlementBatches(int year, int month)
        {
            var touched = await _settlementService.GenerateMonthlyBatchesAsync(year, month);

            TempData["Message"] = touched == 0
                ? $"{year}/{month} 沒有需要結算的新交易。"
                : $"已產生／更新 {touched} 筆 {year}/{month} 結算批次。";

            return RedirectToAction("Settlements");
        }
    }
}
