using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Yustore.Data;
using Yustore.Enums;
using Yustore.Models.Entities;
using Yustore.ViewModels;

namespace Yustore.Controllers
{
    // 登入才能評分，但三種角色都可以用
    // 所以這裡用 [Authorize] 而不是 [OwnerOnly] 之類的
    [Authorize]
    public class ReviewController : Controller
    {
        private readonly AppDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;

        public ReviewController(
            AppDbContext db,
            UserManager<ApplicationUser> userManager)
        {
            _db = db;
            _userManager = userManager;
        }

        // ════════════════════════════════════════
        // 查看某筆訂單需要評分的對象
        // ════════════════════════════════════════

        // GET: /Review/OrderReviews/5
        public async Task<IActionResult> OrderReviews(int orderId)
        {
            var user = await _userManager.GetUserAsync(User);

            var order = await _db.Orders
                .Include(o => o.Restaurant)
                    .ThenInclude(r => r.Owner)
                .Include(o => o.Delivery)
                    .ThenInclude(d => d!.Driver)
                .Include(o => o.Customer)
                .Include(o => o.Reviews)
                .FirstOrDefaultAsync(o => o.Id == orderId);

            if (order == null)
                return NotFound();

            // 只有訂單相關人員才能評分
            bool isCustomer = order.CustomerId == user!.Id;
            bool isDriver = order.Delivery?.DriverId == user.Id;
            bool isOwner = order.Restaurant.OwnerId == user.Id;

            if (!isCustomer && !isDriver && !isOwner)
                return Forbid();

            // 只有已送達或完成的訂單才能評分
            if (order.Status != OrderStatus.已送達 && order.Status != OrderStatus.完成)
            {
                TempData["Error"] = "訂單尚未完成，無法評分。";
                return RedirectToAction("Index", GetRedirectController(user.Role));
            }

            var model = new OrderReviewViewModel
            {
                OrderId = order.Id,
                OrderNumber = order.OrderNumber,
                PendingReviews = GetReviewTargets(order, user).Select(t => new PendingReviewItem
                {
                    TargetUserId = t.TargetUserId,
                    TargetUserName = t.TargetUserName,
                    TargetType = t.TargetType,
                    AlreadyReviewed = order.Reviews.Any(r =>
                        r.ReviewerId == user.Id &&
                        r.TargetUserId == t.TargetUserId)
                }).ToList()
            };

            return View(model);
        }

        // ════════════════════════════════════════
        // V-01 修復：伺服器端依「訂單 + 目前登入者」推導出合法的評分對象清單，
        // GET / POST Create 都必須拿這份清單驗證，不能相信表單/查詢字串傳來的 TargetUserId / TargetType。
        // ════════════════════════════════════════
        private static List<(string TargetUserId, string TargetUserName, ReviewTargetType TargetType)> GetReviewTargets(
            Order order, ApplicationUser user)
        {
            bool isCustomer = order.CustomerId == user.Id;
            bool isDriver = order.Delivery?.DriverId == user.Id;
            bool isOwner = order.Restaurant.OwnerId == user.Id;

            var targets = new List<(string, string, ReviewTargetType)>();

            if (isCustomer)
            {
                // 顧客評：老闆 + 外送師
                targets.Add((order.Restaurant.OwnerId, order.Restaurant.Owner.FullName, ReviewTargetType.老闆));

                if (order.Delivery?.Driver != null)
                    targets.Add((order.Delivery.DriverId, order.Delivery.Driver.FullName, ReviewTargetType.外送師));
            }
            else if (isDriver)
            {
                // 外送師評：顧客 + 老闆
                targets.Add((order.CustomerId, order.Customer.FullName, ReviewTargetType.顧客));
                targets.Add((order.Restaurant.OwnerId, order.Restaurant.Owner.FullName, ReviewTargetType.老闆));
            }
            else if (isOwner)
            {
                // 老闆評：顧客 + 外送師
                targets.Add((order.CustomerId, order.Customer.FullName, ReviewTargetType.顧客));

                if (order.Delivery?.Driver != null)
                    targets.Add((order.Delivery.DriverId, order.Delivery.Driver.FullName, ReviewTargetType.外送師));
            }
            // 三種身分都不是（跟這筆訂單完全無關）→ 回傳空清單，等於沒有任何合法評分對象

            return targets;
        }

        // 載入 Create 需要的訂單資料，並統一做「使用者是否為訂單相關人員」「訂單狀態是否可評分」
        // 「targetUserId 是否為合法評分對象」三項檢查。任何一項不通過就回傳對應的 IActionResult。
        private async Task<(Order? Order, ApplicationUser? User, ReviewTargetType TargetType, IActionResult? Error)>
            ValidateReviewRequestAsync(int orderId, string targetUserId)
        {
            var user = await _userManager.GetUserAsync(User);

            var order = await _db.Orders
                .Include(o => o.Restaurant)
                    .ThenInclude(r => r.Owner)
                .Include(o => o.Delivery)
                    .ThenInclude(d => d!.Driver)
                .Include(o => o.Customer)
                .FirstOrDefaultAsync(o => o.Id == orderId);

            if (order == null || user == null)
                return (null, null, default, NotFound());

            // 訂單狀態必須是已送達或完成才能評分（連「待付款」都能評分是 V-01 的一部分）
            if (order.Status != OrderStatus.已送達 && order.Status != OrderStatus.完成)
            {
                TempData["Error"] = "訂單尚未完成，無法評分。";
                return (null, null, default, RedirectToAction("Index", GetRedirectController(user.Role)));
            }

            // targetUserId 必須是「這筆訂單、這個登入者」合法能評分的對象之一，
            // TargetType 一律由伺服器依這份清單推導，不採用表單/查詢字串傳入的值
            var target = GetReviewTargets(order, user)
                .FirstOrDefault(t => t.TargetUserId == targetUserId);

            if (target.TargetUserId == null)
                return (null, null, default, Forbid());

            return (order, user, target.TargetType, null);
        }

        // ════════════════════════════════════════
        // 評分頁面
        // ════════════════════════════════════════

        // GET: /Review/Create?orderId=1&targetUserId=xxx
        // 注意：targetType 不再由這裡的參數決定，一律由伺服器依訂單關聯推導（V-01 修復）
        [HttpGet]
        public async Task<IActionResult> Create(int orderId, string targetUserId)
        {
            var (order, user, targetType, error) = await ValidateReviewRequestAsync(orderId, targetUserId);
            if (error != null)
                return error;

            // 檢查是否已經評過
            var alreadyReviewed = await _db.Reviews.AnyAsync(r =>
                r.OrderId == orderId &&
                r.ReviewerId == user!.Id &&
                r.TargetUserId == targetUserId);

            if (alreadyReviewed)
            {
                TempData["Error"] = "你已經評過這位使用者了！";
                return RedirectToAction("OrderReviews", new { orderId });
            }

            var targetUser = await _userManager.FindByIdAsync(targetUserId);
            if (targetUser == null)
                return NotFound();

            var model = new ReviewViewModel
            {
                OrderId = orderId,
                OrderNumber = order!.OrderNumber,
                TargetUserId = targetUserId,
                TargetUserName = targetUser.FullName,
                TargetType = targetType
            };

            return View(model);
        }

        // POST: /Review/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(ReviewViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            // model.TargetType 來自表單，不可信任；重新驗證關聯人身分並由伺服器推導正確的 TargetType（V-01 修復）
            var (order, user, targetType, error) = await ValidateReviewRequestAsync(model.OrderId, model.TargetUserId);
            if (error != null)
                return error;

            // 再次確認沒有重複評分
            var alreadyReviewed = await _db.Reviews.AnyAsync(r =>
                r.OrderId == model.OrderId &&
                r.ReviewerId == user!.Id &&
                r.TargetUserId == model.TargetUserId);

            if (alreadyReviewed)
            {
                TempData["Error"] = "你已經評過這位使用者了！";
                return RedirectToAction("OrderReviews", new { orderId = model.OrderId });
            }

            var review = new Review
            {
                OrderId = model.OrderId,
                ReviewerId = user!.Id,
                TargetUserId = model.TargetUserId,
                TargetType = targetType,
                Stars = model.Stars,
                Comment = model.Comment
            };

            _db.Reviews.Add(review);

            // 如果這筆訂單的所有評分都完成了，把訂單狀態改成「完成」
            // （order 是 ValidateReviewRequestAsync 已經查過、且被同一個 _db 追蹤的實體，不用重查）
            if (order!.Status == OrderStatus.已送達)
            {
                order.Status = OrderStatus.完成;
                order.CompletedAt = DateTime.Now;
            }

            await _db.SaveChangesAsync();

            TempData["Message"] = $"已成功評分！";
            return RedirectToAction("OrderReviews", new { orderId = model.OrderId });
        }

        // 根據角色決定返回哪個 Controller
        private string GetRedirectController(UserRole role) => role switch
        {
            UserRole.老闆 => "Owner",
            UserRole.外送師 => "Driver",
            _ => "Customer"
        };
    }
}