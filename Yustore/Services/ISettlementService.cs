using Yustore.Models;
using Yustore.Models.Entities;

namespace Yustore.Services
{
    // M4 修復：外送員完成送達時建立分潤明細的邏輯（原本寫在 DriverController.CompleteOrder），
    // 加上 Admin 用的月結批次產生/查詢功能。商業模式見 docs/PRD-v2.md §4：
    // 餐費抽成 15% 歸平台，剩下歸店家；外送費全額歸外送員。
    public interface ISettlementService
    {
        Task<OrderTransaction> CreateForDeliveryAsync(
            int orderId, int restaurantId, decimal foodTotal, decimal deliveryFee, string driverId);

        // 把某個月份、還沒被納入任何批次的 OrderTransaction，依收款人（老闆/外送員）各自加總，
        // 產生新的 SettlementBatch。已經有批次的收款人/月份會被跳過（見 unique 索引）。
        Task<int> GenerateMonthlyBatchesAsync(int year, int month);

        Task<PagedResult<SettlementBatch>> GetBatchesAsync(int page, string? payeeId = null);

        Task<PagedResult<OrderTransaction>> GetTransactionsForPayeeAsync(string payeeId, int page);
    }
}
