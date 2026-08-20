using Yustore.Models.Entities;

namespace Yustore.Services
{
    // M3 修復（§3.1 Service 層拆分）：外送員完成送達時建立結算記錄的邏輯，原本寫在
    // DriverController.CompleteOrder 裡。目前只有「寫入」，還沒有查詢/月結介面
    // （ASSESSMENT.md 提到「Settlement 只寫不讀」），完整的分潤/月結批次設計留給 M4
    // （見 docs/PRD-v2.md §4 商業模式），這裡先把現有的建立邏輯抽出來、有測試涵蓋。
    public interface ISettlementService
    {
        Task<Settlement> CreateForDeliveryAsync(int orderId, int restaurantId, decimal foodTotal, string driverId);
    }
}
