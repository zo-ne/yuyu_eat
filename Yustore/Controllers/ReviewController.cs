using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Yustore.Enums;
using Yustore.Models.Entities;
using Yustore.Services;
using Yustore.ViewModels;

namespace Yustore.Controllers
{
    // 登入才能評分，但三種角色都可以用
    // 所以這裡用 [Authorize] 而不是 [RoleRequired(...)] 之類的
    [Authorize]
    public class ReviewController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IReviewService _reviewService;

        public ReviewController(
            UserManager<ApplicationUser> userManager,
            IReviewService reviewService)
        {
            _userManager = userManager;
            _reviewService = reviewService;
        }

        // ════════════════════════════════════════
        // 查看某筆訂單需要評分的對象
        // ════════════════════════════════════════

        // GET: /Review/OrderReviews/5
        // M3 修復（§3.1 Service 層拆分）：授權判定與訂單查詢搬進 IReviewService，
        // 這裡只負責把 Service 的結果轉成對應的 HTTP 回應。
        public async Task<IActionResult> OrderReviews(int orderId)
        {
            var user = await _userManager.GetUserAsync(User);
            var result = await _reviewService.GetOrderReviewsAsync(orderId, user!);

            switch (result.Result)
            {
                case ReviewOpResult.NotFound:
                    return NotFound();
                case ReviewOpResult.Forbidden:
                    return Forbid();
                case ReviewOpResult.NotYetCompleted:
                    TempData["Error"] = "訂單尚未完成，無法評分。";
                    return RedirectToAction("Index", GetRedirectController(user!.Role));
            }

            return View(result.Model);
        }

        // ════════════════════════════════════════
        // V-01 修復：伺服器端依「訂單 + 目前登入者」推導出合法的評分對象清單，
        // GET / POST Create 都必須拿這份清單驗證，不能相信表單/查詢字串傳來的 TargetUserId / TargetType。
        // 這兩個純邏輯方法留在這裡（不搬進 IReviewService）是刻意的：ReviewService 跟
        // ReviewAuthorizationTests（M2）都直接呼叫這兩個 internal static 方法，
        // 純函式不需要依賴 DbContext，測試起來也不用 mock 任何東西。
        // ════════════════════════════════════════
        internal static List<(string TargetUserId, string TargetUserName, ReviewTargetType TargetType)> GetReviewTargets(
            Order order, ApplicationUser user)
        {
            bool isCustomer = order.CustomerId == user.Id;
            bool isDriver = order.Delivery?.DriverId == user.Id;
            bool isOwner = order.Restaurant.OwnerId == user.Id;

            var targets = new List<(string, string, ReviewTargetType)>();

            if (isCustomer)
            {
                // 顧客評：老闆 + 外送師
                targets.Add((order.Restaurant.OwnerId, order.Restaurant.Owner.FullName, ReviewTargetType.Owner));

                if (order.Delivery?.Driver != null)
                    targets.Add((order.Delivery.DriverId, order.Delivery.Driver.FullName, ReviewTargetType.Driver));
            }
            else if (isDriver)
            {
                // 外送師評：顧客 + 老闆
                targets.Add((order.CustomerId, order.Customer.FullName, ReviewTargetType.Customer));
                targets.Add((order.Restaurant.OwnerId, order.Restaurant.Owner.FullName, ReviewTargetType.Owner));
            }
            else if (isOwner)
            {
                // 老闆評：顧客 + 外送師
                targets.Add((order.CustomerId, order.Customer.FullName, ReviewTargetType.Customer));

                if (order.Delivery?.Driver != null)
                    targets.Add((order.Delivery.DriverId, order.Delivery.Driver.FullName, ReviewTargetType.Driver));
            }
            // 三種身分都不是（跟這筆訂單完全無關）→ 回傳空清單，等於沒有任何合法評分對象

            return targets;
        }

        // R-4 修復：列出這筆訂單「全部」應該存在的評分配對（誰評誰），跟 GetReviewTargets 不同的是
        // 這裡不是站在某一個使用者的角度，而是列出三方彼此互評的完整清單，用來判斷訂單能不能轉「完成」。
        internal static List<(string ReviewerId, string TargetUserId)> GetAllRequiredReviewPairs(Order order)
        {
            var pairs = new List<(string, string)>
            {
                (order.CustomerId, order.Restaurant.OwnerId), // 顧客 → 老闆
                (order.Restaurant.OwnerId, order.CustomerId),  // 老闆 → 顧客
            };

            if (order.Delivery?.DriverId != null)
            {
                pairs.Add((order.CustomerId, order.Delivery.DriverId));         // 顧客 → 外送師
                pairs.Add((order.Delivery.DriverId, order.CustomerId));         // 外送師 → 顧客
                pairs.Add((order.Delivery.DriverId, order.Restaurant.OwnerId)); // 外送師 → 老闆
                pairs.Add((order.Restaurant.OwnerId, order.Delivery.DriverId)); // 老闆 → 外送師
            }

            return pairs;
        }

        // ════════════════════════════════════════
        // 評分頁面
        // ════════════════════════════════════════

        // GET: /Review/Create?orderId=1&targetUserId=xxx
        // 注意：targetType 不再由這裡的參數決定，一律由伺服器依訂單關聯推導（V-01 修復）
        [HttpGet]
        public async Task<IActionResult> Create(int orderId, string targetUserId)
        {
            var user = await _userManager.GetUserAsync(User);
            var result = await _reviewService.PrepareReviewAsync(orderId, targetUserId, user!);

            switch (result.Result)
            {
                case ReviewOpResult.NotFound:
                    return NotFound();
                case ReviewOpResult.Forbidden:
                    return Forbid();
                case ReviewOpResult.NotYetCompleted:
                    TempData["Error"] = "訂單尚未完成，無法評分。";
                    return RedirectToAction("Index", GetRedirectController(user!.Role));
                case ReviewOpResult.AlreadyReviewed:
                    TempData["Error"] = "你已經評過這位使用者了！";
                    return RedirectToAction("OrderReviews", new { orderId });
            }

            return View(result.Model);
        }

        // POST: /Review/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(ReviewViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            var user = await _userManager.GetUserAsync(User);

            // model.TargetType 來自表單，不可信任；IReviewService 內部會重新驗證關聯人身分
            // 並由伺服器推導正確的 TargetType（V-01 修復）
            var result = await _reviewService.SubmitReviewAsync(
                model.OrderId, model.TargetUserId, user!, model.Stars, model.Comment);

            switch (result)
            {
                case ReviewOpResult.NotFound:
                    return NotFound();
                case ReviewOpResult.Forbidden:
                    return Forbid();
                case ReviewOpResult.NotYetCompleted:
                    TempData["Error"] = "訂單尚未完成，無法評分。";
                    return RedirectToAction("Index", GetRedirectController(user!.Role));
                case ReviewOpResult.AlreadyReviewed:
                    TempData["Error"] = "你已經評過這位使用者了！";
                    return RedirectToAction("OrderReviews", new { orderId = model.OrderId });
            }

            TempData["Message"] = "已成功評分！";
            return RedirectToAction("OrderReviews", new { orderId = model.OrderId });
        }

        // 根據角色決定返回哪個 Controller
        private string GetRedirectController(UserRole role) => role switch
        {
            UserRole.Owner => "Owner",
            UserRole.Driver => "Driver",
            _ => "Customer"
        };
    }
}
