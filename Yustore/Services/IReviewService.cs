using Yustore.Models.Entities;
using Yustore.ViewModels;

namespace Yustore.Services
{
    // M3 修復（§3.1 Service 層拆分）：評價的核心邏輯（誰能評誰、訂單狀態是否可評分、
    // 評完是否要把訂單轉「完成」）原本寫在 ReviewController 裡。
    // 授權判定的純邏輯（誰該評誰）已經是 ReviewController 裡的 internal static 方法
    // （GetReviewTargets / GetAllRequiredReviewPairs，M2 就有測試涵蓋），這裡把「跑 DB」
    // 的部分（查訂單、建立評價、判斷是否轉完成）搬過來，Controller 只剩 HTTP 層的事。
    public interface IReviewService
    {
        Task<OrderReviewsResult> GetOrderReviewsAsync(int orderId, ApplicationUser user);

        Task<PrepareReviewResult> PrepareReviewAsync(int orderId, string targetUserId, ApplicationUser user);

        Task<ReviewOpResult> SubmitReviewAsync(
            int orderId, string targetUserId, ApplicationUser reviewer, int stars, string? comment);
    }

    public enum ReviewOpResult
    {
        Success,
        NotFound,
        Forbidden,
        NotYetCompleted,
        AlreadyReviewed,
    }

    public record OrderReviewsResult(ReviewOpResult Result, OrderReviewViewModel? Model);

    public record PrepareReviewResult(ReviewOpResult Result, ReviewViewModel? Model);
}
