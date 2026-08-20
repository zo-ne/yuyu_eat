using Yustore.Enums;
using Yustore.Models.Entities;
using Yustore.ViewModels;

namespace Yustore.Services
{
    // M3 修復（§3.1/§3.2 Service 層拆分）：訂單的核心業務規則（結帳重新驗價、狀態轉換白名單、
    // 訂單編號產生）原本散落在 CustomerController 跟 OwnerController 的 Action 裡。
    // Controller 應該只負責接資料、呼叫 Service、回傳結果，這裡把邏輯搬過來集中管理，
    // 也才有辦法脫離 HttpContext 寫單元測試。
    public interface IOrderService
    {
        Task<CheckoutResult> CheckoutAsync(string customerId, CartViewModel cart, string? deliveryAddress, string? note);

        Task<Order?> ConfirmPaymentAsync(int orderId, string customerId);

        Task<StatusUpdateResult> UpdateOwnerStatusAsync(int orderId, string restaurantOwnerId, OrderStatus newStatus);
    }

    public record CheckoutResult(bool Success, string? ErrorMessage, Order? Order)
    {
        public static CheckoutResult Fail(string message) => new(false, message, null);
        public static CheckoutResult Ok(Order order) => new(true, null, order);
    }

    public enum StatusUpdateFailureReason
    {
        None,
        NotFound,
        InvalidStatus,
        InvalidTransition,
    }

    public record StatusUpdateResult(bool Success, StatusUpdateFailureReason FailureReason, string? ErrorMessage, Order? Order)
    {
        public static StatusUpdateResult Fail(StatusUpdateFailureReason reason, string message) =>
            new(false, reason, message, null);

        public static StatusUpdateResult Ok(Order order) =>
            new(true, StatusUpdateFailureReason.None, null, order);
    }
}
