using Yustore.Enums;

namespace Yustore.Models.Entities
{
    // 月結批次：Admin 手動觸發「產生本月結算批次」時，把某個月份、某一位收款人
    // （老闆或外送員）還沒結算的 OrderTransaction 加總成一筆。
    public class SettlementBatch
    {
        public int Id { get; set; }
        public int Year { get; set; }
        public int Month { get; set; }
        public decimal TotalAmount { get; set; }
        public SettlementStatus Status { get; set; } = SettlementStatus.Unsettled;
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime? SettledAt { get; set; }

        // FK：收款人可能是老闆也可能是外送員，同一個 ApplicationUser 表，用 Role 區分
        public string PayeeId { get; set; } = string.Empty;
        public ApplicationUser Payee { get; set; } = null!;

        public ICollection<OrderTransaction> OwnerTransactions { get; set; } = new List<OrderTransaction>();
        public ICollection<OrderTransaction> DriverTransactions { get; set; } = new List<OrderTransaction>();
    }
}
