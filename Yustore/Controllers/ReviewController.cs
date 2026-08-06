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
                PendingReviews = new List<PendingReviewItem>()
            };

            // 根據角色決定要評分的對象
            if (isCustomer)
            {
                // 顧客評：老闆 + 外送師
                model.PendingReviews.Add(new PendingReviewItem
                {
                    TargetUserId = order.Restaurant.OwnerId,
                    TargetUserName = order.Restaurant.Owner.FullName,
                    TargetType = ReviewTargetType.老闆,
                    AlreadyReviewed = order.Reviews.Any(r =>
                        r.ReviewerId == user.Id &&
                        r.TargetUserId == order.Restaurant.OwnerId)
                });

                if (order.Delivery?.Driver != null)
                {
                    model.PendingReviews.Add(new PendingReviewItem
                    {
                        TargetUserId = order.Delivery.DriverId,
                        TargetUserName = order.Delivery.Driver.FullName,
                        TargetType = ReviewTargetType.外送師,
                        AlreadyReviewed = order.Reviews.Any(r =>
                            r.ReviewerId == user.Id &&
                            r.TargetUserId == order.Delivery.DriverId)
                    });
                }
            }
            else if (isDriver)
            {
                // 外送師評：顧客 + 老闆
                model.PendingReviews.Add(new PendingReviewItem
                {
                    TargetUserId = order.CustomerId,
                    TargetUserName = order.Customer.FullName,
                    TargetType = ReviewTargetType.顧客,
                    AlreadyReviewed = order.Reviews.Any(r =>
                        r.ReviewerId == user.Id &&
                        r.TargetUserId == order.CustomerId)
                });

                model.PendingReviews.Add(new PendingReviewItem
                {
                    TargetUserId = order.Restaurant.OwnerId,
                    TargetUserName = order.Restaurant.Owner.FullName,
                    TargetType = ReviewTargetType.老闆,
                    AlreadyReviewed = order.Reviews.Any(r =>
                        r.ReviewerId == user.Id &&
                        r.TargetUserId == order.Restaurant.OwnerId)
                });
            }
            else if (isOwner)
            {
                // 老闆評：顧客 + 外送師
                model.PendingReviews.Add(new PendingReviewItem
                {
                    TargetUserId = order.CustomerId,
                    TargetUserName = order.Customer.FullName,
                    TargetType = ReviewTargetType.顧客,
                    AlreadyReviewed = order.Reviews.Any(r =>
                        r.ReviewerId == user.Id &&
                        r.TargetUserId == order.CustomerId)
                });

                if (order.Delivery?.Driver != null)
                {
                    model.PendingReviews.Add(new PendingReviewItem
                    {
                        TargetUserId = order.Delivery.DriverId,
                        TargetUserName = order.Delivery.Driver.FullName,
                        TargetType = ReviewTargetType.外送師,
                        AlreadyReviewed = order.Reviews.Any(r =>
                            r.ReviewerId == user.Id &&
                            r.TargetUserId == order.Delivery.DriverId)
                    });
                }
            }

            return View(model);
        }

        // ════════════════════════════════════════
        // 評分頁面
        // ════════════════════════════════════════

        // GET: /Review/Create?orderId=1&targetUserId=xxx&targetType=0
        [HttpGet]
        public async Task<IActionResult> Create(
            int orderId, string targetUserId, ReviewTargetType targetType)
        {
            var user = await _userManager.GetUserAsync(User);

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

            var order = await _db.Orders
                .FirstOrDefaultAsync(o => o.Id == orderId);

            var targetUser = await _userManager.FindByIdAsync(targetUserId);

            if (order == null || targetUser == null)
                return NotFound();

            var model = new ReviewViewModel
            {
                OrderId = orderId,
                OrderNumber = order.OrderNumber,
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

            var user = await _userManager.GetUserAsync(User);

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
                TargetType = model.TargetType,
                Stars = model.Stars,
                Comment = model.Comment
            };

            _db.Reviews.Add(review);

            // 如果這筆訂單的所有評分都完成了，把訂單狀態改成「完成」
            var order = await _db.Orders
                .Include(o => o.Reviews)
                .FirstOrDefaultAsync(o => o.Id == model.OrderId);

            if (order?.Status == OrderStatus.已送達)
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