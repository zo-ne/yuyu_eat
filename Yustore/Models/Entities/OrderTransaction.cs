namespace Yustore.Models.Entities
{
    // M4 修復：原本的 Settlement 資料表「一訂單一列」，但欄位卻是 Year/Month/Status 這種
    // 月結批次的語意，兩件事混在一起。拆成兩層：OrderTransaction 是每筆訂單的分潤明細
    // （送達當下就產生），SettlementBatch 是月結批次（Admin 手動觸發，把還沒結算的
    // OrderTransaction 依收款人彙總成一筆）。見 docs/PRD-v2.md §4 商業模式。
    public class OrderTransaction
    {
        public int Id { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public decimal GrossAmount { get; set; }      // 顧客付的總額（餐費+外送費）
        public decimal PlatformFee { get; set; }       // 平台抽成（餐費 × 15%）
        public decimal RestaurantPayout { get; set; }  // 應付店家（餐費 - 平台抽成）
        public decimal DriverPayout { get; set; }      // 應付外送師（外送費，全額）

        // FK
        public int OrderId { get; set; }
        public Order Order { get; set; } = null!;

        public string OwnerId { get; set; } = string.Empty;
        public ApplicationUser Owner { get; set; } = null!;

        public string DriverId { get; set; } = string.Empty;
        public ApplicationUser Driver { get; set; } = null!;

        // 這筆交易的店家分潤/外送員分潤分別被納入哪個月結批次，在批次產生前都是 null。
        // 一筆交易有兩個收款人（店家 + 外送員），所以要兩個各自獨立的 FK，
        // 不能只用一個「SettlementBatchId」（那樣會分不清是哪一邊的批次）。
        public int? OwnerSettlementBatchId { get; set; }
        public SettlementBatch? OwnerSettlementBatch { get; set; }

        public int? DriverSettlementBatchId { get; set; }
        public SettlementBatch? DriverSettlementBatch { get; set; }
    }
}
