using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Yustore.Controllers;
using Yustore.Data;
using Yustore.Enums;
using Yustore.Models.Entities;
using Yustore.ViewModels;

namespace Yustore.Services
{
    public class ReviewService : IReviewService
    {
        private readonly AppDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;

        public ReviewService(AppDbContext db, UserManager<ApplicationUser> userManager)
        {
            _db = db;
            _userManager = userManager;
        }

        public async Task<OrderReviewsResult> GetOrderReviewsAsync(int orderId, ApplicationUser user)
        {
            var order = await LoadOrderAsync(orderId, includeReviews: true);
            if (order == null)
                return new OrderReviewsResult(ReviewOpResult.NotFound, null);

            bool isCustomer = order.CustomerId == user.Id;
            bool isDriver = order.Delivery?.DriverId == user.Id;
            bool isOwner = order.Restaurant.OwnerId == user.Id;

            if (!isCustomer && !isDriver && !isOwner)
                return new OrderReviewsResult(ReviewOpResult.Forbidden, null);

            if (order.Status != OrderStatus.Delivered && order.Status != OrderStatus.Completed)
                return new OrderReviewsResult(ReviewOpResult.NotYetCompleted, null);

            var model = new OrderReviewViewModel
            {
                OrderId = order.Id,
                OrderNumber = order.OrderNumber,
                PendingReviews = ReviewController.GetReviewTargets(order, user).Select(t => new PendingReviewItem
                {
                    TargetUserId = t.TargetUserId,
                    TargetUserName = t.TargetUserName,
                    TargetType = t.TargetType,
                    AlreadyReviewed = order.Reviews.Any(r =>
                        r.ReviewerId == user.Id &&
                        r.TargetUserId == t.TargetUserId),
                }).ToList(),
            };

            return new OrderReviewsResult(ReviewOpResult.Success, model);
        }

        public async Task<PrepareReviewResult> PrepareReviewAsync(int orderId, string targetUserId, ApplicationUser user)
        {
            var (order, targetType, error) = await ValidateReviewRequestAsync(orderId, targetUserId, user);
            if (error != ReviewOpResult.Success)
                return new PrepareReviewResult(error, null);

            var alreadyReviewed = await _db.Reviews.AnyAsync(r =>
                r.OrderId == orderId && r.ReviewerId == user.Id && r.TargetUserId == targetUserId);
            if (alreadyReviewed)
                return new PrepareReviewResult(ReviewOpResult.AlreadyReviewed, null);

            var targetUser = await _userManager.FindByIdAsync(targetUserId);
            if (targetUser == null)
                return new PrepareReviewResult(ReviewOpResult.NotFound, null);

            var model = new ReviewViewModel
            {
                OrderId = orderId,
                OrderNumber = order!.OrderNumber,
                TargetUserId = targetUserId,
                TargetUserName = targetUser.FullName,
                TargetType = targetType,
            };

            return new PrepareReviewResult(ReviewOpResult.Success, model);
        }

        public async Task<ReviewOpResult> SubmitReviewAsync(
            int orderId, string targetUserId, ApplicationUser reviewer, int stars, string? comment)
        {
            var (order, targetType, error) = await ValidateReviewRequestAsync(orderId, targetUserId, reviewer);
            if (error != ReviewOpResult.Success)
                return error;

            var alreadyReviewed = await _db.Reviews.AnyAsync(r =>
                r.OrderId == orderId && r.ReviewerId == reviewer.Id && r.TargetUserId == targetUserId);
            if (alreadyReviewed)
                return ReviewOpResult.AlreadyReviewed;

            var review = new Review
            {
                OrderId = orderId,
                ReviewerId = reviewer.Id,
                TargetUserId = targetUserId,
                TargetType = targetType,
                Stars = stars,
                Comment = comment,
            };

            _db.Reviews.Add(review);

            // R-4 修復：原本只要「任一人」評分就把訂單轉「完成」，
            // 改成「這筆訂單所有應評對象都評完」才轉換。
            if (order!.Status == OrderStatus.Delivered)
            {
                var requiredPairs = ReviewController.GetAllRequiredReviewPairs(order);
                var donePairs = order.Reviews
                    .Select(r => (r.ReviewerId, r.TargetUserId))
                    .Append((review.ReviewerId, review.TargetUserId)) // 這筆剛加進 _db 但還沒存檔的評分也要算進去
                    .ToHashSet();

                if (requiredPairs.All(p => donePairs.Contains(p)))
                {
                    order.Status = OrderStatus.Completed;
                    order.CompletedAt = DateTime.Now;
                }
            }

            await _db.SaveChangesAsync();

            return ReviewOpResult.Success;
        }

        // 載入 Create/OrderReviews 都需要的訂單資料，並統一做「使用者是否為訂單相關人員」
        // 「訂單狀態是否可評分」「targetUserId 是否為合法評分對象」三項檢查。
        private async Task<(Order? Order, ReviewTargetType TargetType, ReviewOpResult Error)>
            ValidateReviewRequestAsync(int orderId, string targetUserId, ApplicationUser user)
        {
            var order = await LoadOrderAsync(orderId, includeReviews: true);
            if (order == null)
                return (null, default, ReviewOpResult.NotFound);

            // 訂單狀態必須是已送達或完成才能評分（連「待付款」都能評分是 V-01 的一部分）
            if (order.Status != OrderStatus.Delivered && order.Status != OrderStatus.Completed)
                return (null, default, ReviewOpResult.NotYetCompleted);

            // targetUserId 必須是「這筆訂單、這個登入者」合法能評分的對象之一，
            // TargetType 一律由伺服器依這份清單推導，不採用表單/查詢字串傳入的值（V-01 修復）
            var target = ReviewController.GetReviewTargets(order, user)
                .FirstOrDefault(t => t.TargetUserId == targetUserId);

            if (target.TargetUserId == null)
                return (null, default, ReviewOpResult.Forbidden);

            return (order, target.TargetType, ReviewOpResult.Success);
        }

        private Task<Order?> LoadOrderAsync(int orderId, bool includeReviews)
        {
            var query = _db.Orders
                .Include(o => o.Restaurant)
                    .ThenInclude(r => r.Owner)
                .Include(o => o.Delivery)
                    .ThenInclude(d => d!.Driver)
                .Include(o => o.Customer)
                .AsQueryable();

            if (includeReviews)
                query = query.Include(o => o.Reviews);

            return query.FirstOrDefaultAsync(o => o.Id == orderId)!;
        }
    }
}
